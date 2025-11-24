#!/usr/bin/env node

/**
 * Simple MCP Server for Demo
 * Uses stdio transport, compatible with Node.js v24
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
    CallToolRequestSchema,
    ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

// Create server instance
const server = new Server(
    {
        name: "simple-demo-server",
        version: "1.0.0",
    },
    {
        capabilities: {
            tools: {},
        },
    }
);

// Register tool: get_weather
server.setRequestHandler(ListToolsRequestSchema, async () => {
    return {
        tools: [
            {
                name: "get_weather",
                description: "Get weather information for a city",
                inputSchema: {
                    type: "object",
                    properties: {
                        city: {
                            type: "string",
                            description: "City name",
                        },
                    },
                    required: ["city"],
                },
            },
            {
                name: "calculate_sum",
                description: "Calculate sum of two numbers",
                inputSchema: {
                    type: "object",
                    properties: {
                        a: {
                            type: "number",
                            description: "First number",
                        },
                        b: {
                            type: "number",
                            description: "Second number",
                        },
                    },
                    required: ["a", "b"],
                },
            },
        ],
    };
});

// Handle tool execution
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    switch (name) {
        case "get_weather": {
            const city = args.city;
            // Simulate weather data
            const weather = {
                city,
                temperature: Math.floor(Math.random() * 30) + 10,
                condition: ["Sunny", "Cloudy", "Rainy"][Math.floor(Math.random() * 3)],
                humidity: Math.floor(Math.random() * 50) + 30,
            };
            return {
                content: [
                    {
                        type: "text",
                        text: `Weather in ${weather.city}: ${weather.temperature}°C, ${weather.condition}, Humidity: ${weather.humidity}%`,
                    },
                ],
            };
        }

        case "calculate_sum": {
            const sum = args.a + args.b;
            return {
                content: [
                    {
                        type: "text",
                        text: `${args.a} + ${args.b} = ${sum}`,
                    },
                ],
            };
        }

        default:
            throw new Error(`Unknown tool: ${name}`);
    }
});

// Start server with stdio transport
async function main() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("Simple MCP Server running on stdio");
}

main().catch((error) => {
    console.error("Server error:", error);
    process.exit(1);
});
