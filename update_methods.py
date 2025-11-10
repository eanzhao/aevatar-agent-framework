#!/usr/bin/env python3

import os
import re

def update_file(file_path):
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()

        # Replace Success method calls
        content = re.sub(r'AevatarAIToolResult\.Success\(', 'AevatarAIToolResult.CreateSuccess(', content)

        # Replace Failure method calls
        content = re.sub(r'AevatarAIToolResult\.Failure\(', 'AevatarAIToolResult.CreateFailure(', content)

        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(content)

        print(f"Updated: {file_path}")
        return True
    except Exception as e:
        print(f"Error updating {file_path}: {e}")
        return False

# Update specific files
files_to_update = [
    "/Users/zhaoyiqi/Code/aevatar-agent-framework/src/Aevatar.Agents.AI.MEAI/Examples/SampleAgentWithTools.cs",
    "/Users/zhaoyiqi/Code/aevatar-agent-framework/src/Aevatar.Agents.AI.MEAI/MEAIGAgentBase.cs",
    "/Users/zhaoyiqi/Code/aevatar-agent-framework/test/Aevatar.Agents.AI.Tests/InterfaceDefinitionTests.cs",
    "/Users/zhaoyiqi/Code/aevatar-agent-framework/test/Aevatar.Agents.AI.Tests/MEAIGAgentBaseTests.cs"
]

for file_path in files_to_update:
    if os.path.exists(file_path):
        update_file(file_path)
    else:
        print(f"File not found: {file_path}")

print("Method name updates completed!")