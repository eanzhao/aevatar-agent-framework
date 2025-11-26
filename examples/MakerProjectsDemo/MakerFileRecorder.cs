using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MakerProjectsDemo.Infrastructure;

public interface IMakerRunRecorder
{
    Task RecordEventAsync(string runId, string type, string source, object content, string? filenameSuffix = null);
}

public class MakerFileRecorder : IMakerRunRecorder
{
    private readonly ConcurrentDictionary<string, int> _sequenceCounters = new();

    public async Task RecordEventAsync(string runId, string type, string source, object content,
        string? filenameSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(runId)) return;

        var date = DateTime.UtcNow.ToString("yyyyMMdd");

        // --- Path Logic Refinement ---
        // Goal: A clean, hierarchical directory structure representing the decomposition tree.
        // Format: runs/date/runId/1_2_3/
        // Where "1_2_3" means Step 3 of Step 2 of Step 1.

        var relativePath = "";
        var taskId = source;

        // Extract TaskId
        if (content?.GetType().GetProperty("TaskId")?.GetValue(content) is string actualTaskId)
        {
            taskId = actualTaskId;
        }

        // Parse TaskId to build hierarchy
        // Expected Format: root-guid:S01:S02 or root-guid (for level 0)
        // We map "S01" -> "1", "S02" -> "2" for cleaner directory names.
        if (taskId.Contains(":"))
        {
            var parts = taskId.Split(':');
            // parts[0] is root, ignore for relative path
            // parts[1..] are steps like "S01", "S02"
            
            // User pointed out "6/5" structure which means S06/S05 nesting.
            // He wants "每一次递归，按照前面的序号加_和数字，比如1_1，1_2".
            // This means:
            // Level 0: root
            // Level 1 (S01): root/1
            // Level 2 (S01:S02): root/1_2  <-- User asked for this flat style but with hierarchy encoded in name
            
            // "按目录深度体现递归深度" -> This means real folders: 1/2/
            // "按目录深度体现投票轮数" -> This is tricky. Voting happens within a task.
            // Task ID structure reflects RECURSION depth.
            
            // The user's complaint: "S07套S04...很混乱" was about deep nesting like S07/S04/...
            // But then said "人能接受的是0次递归... 按1往后排... 每一次递归... 1_1"
            // Wait, "1_1" usually implies a single folder name "1_1" inside root? Or "1/1_1"?
            
            // Re-reading: "按目录深度体现递归深度" -> This strongly suggests nested folders.
            // "S07套S04...很混乱" might be because "S07" is an ugly name.
            // If we map S07 -> 7, S04 -> 4. We get 7/4/4.
            // The user saw "6套了个5" (6/5). This is actually correct for 6/5 nesting.
            
            // Let's try to align with "1_1".
            // Maybe he wants:
            // Root
            //   1_Proposal...
            //   1/
            //     1_1_Proposal...
            
            // Let's try to make the directory name cumulative?
            // Path: 1/1_2/1_2_3/ ?
            
            // Or maybe just:
            // root/
            //   1/
            //     2/
            // is fine, BUT the folder names should be 1, 2, 3 (sequential) not S07, S04 (which are Step IDs from Plan).
            // Step IDs come from the Plan. If the plan says "Step 7", we use 7.
            // If the user sees "6/5", it means Step 6 -> Step 5.
            
            // If the user wants "1_1", maybe he wants the directory name to be the full path?
            // e.g. runs/date/runId/1_1/
            // But "按目录深度" usually means folder nesting.
            
            // Let's try to implement: 
            // Depth 1: "1"
            // Depth 2: "1/1_1" (Folder "1_1" inside "1") ?
            // Or "1/1" ?
            
            // Let's stick to simple numbers for folders, but make sure they are sorted/sequential if possible?
            // We can't change StepIDs (they are from LLM).
            
            // Let's implement the "1_1" style directory names *nested*.
            // Root -> S01
            // Path: 1
            // Root -> S01 -> S02
            // Path: 1/1_2
            
            var pathSegments = new List<string>();
            var currentPath = new StringBuilder();
            
            var steps = parts.Skip(1).Select(s => 
            {
                if (s.StartsWith("S") && int.TryParse(s.Substring(1), out var n)) return n.ToString();
                return s;
            }).ToList();

            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) currentPath.Append("_");
                currentPath.Append(steps[i]);
                pathSegments.Add(currentPath.ToString());
            }
            
            // If steps is [1, 2]
            // pathSegments: ["1", "1_2"]
            // Result: runs/.../1/1_2/
            
            relativePath = Path.Combine(pathSegments.ToArray());
        }
        else 
        {
            relativePath = "root";
        }

        var dir = Path.Combine("runs", date, runId, relativePath);
        Directory.CreateDirectory(dir);

        // --- Filename Logic Refinement ---
        // Goal: Sequential naming within the folder.
        // Filename: {GlobalSeq}_{Type}_{ShortId}.json

        var seq = _sequenceCounters.AddOrUpdate(runId, 1, (_, c) => c + 1);

        // Clean up Type/Suffix redundancy
        // type is usually "Proposal" or "Consensus"
        // filenameSuffix was "Proposal_ReqId" -> "Proposal_Proposal_..." (Duplicate!)

        var cleanSuffix = filenameSuffix;
        if (!string.IsNullOrWhiteSpace(cleanSuffix) && cleanSuffix.StartsWith(type + "_"))
        {
            cleanSuffix = cleanSuffix.Substring(type.Length + 1); // Remove redundant prefix
        }
        else if (string.IsNullOrWhiteSpace(cleanSuffix))
        {
            cleanSuffix = "Data";
        }

        // Shorten GUIDs in filename for readability
        // If suffix contains a GUID (32 chars hex), shrink it.
        // Heuristic: look for 32-char hex strings
        if (cleanSuffix.Length > 10)
        {
            // Keep it simple: just use the whole suffix but sanitize
        }

        var namePart = cleanSuffix;
        namePart = string.Join("_", namePart.Split(Path.GetInvalidFileNameChars()));

        var filename = $"{seq:D4}_{type}_{namePart}";
        var path = Path.Combine(dir, filename);

        // --- Content Serialization ---
        string textContent;
        if (content is string s)
        {
            textContent = s;
            if (!path.EndsWith(".md") && !path.EndsWith(".txt") && !path.EndsWith(".json"))
            {
                path += ".md";
            }
        }
        else
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            textContent = JsonSerializer.Serialize(content, options);
            if (!path.EndsWith(".json"))
            {
                path += ".json";
            }
        }

        await File.WriteAllTextAsync(path, textContent);
    }
}