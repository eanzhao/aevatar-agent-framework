using Aevatar.Agents;
using Aevatar.Agents.Maker;
using MakerProjectsDemo.Infrastructure;
using MakerProjectsDemo.Projects.Bazi;
using MakerProjectsDemo.Projects.Paper;

namespace MakerProjectsDemo.Projects;

public static class MakerProjectDefinitions
{
    public static MakerProjectSpec CreateBaziSpec() => new()
    {
        Id = "bazi",
        Name = "八字推演",
        Description = "多 Agent 八字推演，涵盖格局拆解、喜忌分析与最终报告综合。",
        Icon = "🌓",
        WorkerCount = 4,
        TaskFactory = (factory, ct) => factory.CreateGAgentActorAsync<BaziMakerTaskAgent>(Guid.NewGuid(), ct),
        WorkerFactory = (factory, ct) => factory.CreateGAgentActorAsync<BaziMakerWorkerAgent>(Guid.NewGuid(), ct),
        OnRunStarting = ctx =>
        {
            var profile = BaziProfile.CreateDemoProfile();
            ctx.SetItem("profile", profile);
            ctx.Timeline.Append("info", "profile", $"主题：{profile.FocusTopic}");
        },
        BuildRootAssignTask = ctx =>
        {
            var profile = ctx.GetItem<BaziProfile>("profile") ?? BaziProfile.CreateDemoProfile();
            var goal = BaziGoalBuilder.BuildGoal(profile);
            return new AssignTaskEvent
            {
                TaskId = ctx.RootTaskId,
                GoalDescription = goal,
                CurrentDepth = 0,
                ContextVariables =
                {
                    { "analysis_topic", "bazi_report" },
                    { "birth_city", profile.Birthplace },
                    { "lunar_day", profile.LunarDayStemBranch }
                }
            };
        }
    };

    public static MakerProjectSpec CreatePaperSpec() => new()
    {
        Id = "paper",
        Name = "论文总结",
        Description = "多 Agent 协作拆解技术论文，并生成结构化总结。",
        Icon = "📄",
        WorkerCount = 5,
        TaskFactory = (factory, ct) => factory.CreateGAgentActorAsync<PaperSummaryTaskAgent>(Guid.NewGuid(), ct),
        WorkerFactory = (factory, ct) => factory.CreateGAgentActorAsync<PaperSummaryWorkerAgent>(Guid.NewGuid(), ct),
        OnRunStarting = ctx =>
        {
            ctx.Timeline.Append("info", "profile", $"Task: Summarize Paper '{PaperContent.Title}'");
        },
        BuildRootAssignTask = ctx => new AssignTaskEvent
        {
            TaskId = ctx.RootTaskId,
            GoalDescription = $"Summarize the paper '{PaperContent.Title}' by decomposing it into logical sections.",
            CurrentDepth = 0,
            ContextVariables =
            {
                { "analysis_topic", "paper_summary" }
            }
        }
    };
}

