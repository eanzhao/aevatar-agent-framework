#!/bin/bash

echo "🧪 Testing Aevatar Agent Framework Demo API"
echo "=========================================="
echo ""

# 等待 API 启动
echo "⏳ Waiting for API to start..."
sleep 2

BASE_URL="https://localhost:7001"

echo "📊 Testing Calculator API..."
echo "----------------------------"

echo "1. Testing Add (10 + 5):"
curl -s -X POST "${BASE_URL}/api/Calculator/add?a=10&b=5" -k | jq .

echo ""
echo "2. Testing Subtract (20 - 8):"
curl -s -X POST "${BASE_URL}/api/Calculator/subtract?a=20&b=8" -k | jq .

echo ""
echo "3. Testing Multiply (6 × 7):"
curl -s -X POST "${BASE_URL}/api/Calculator/multiply?a=6&b=7" -k | jq .

echo ""
echo "4. Testing Divide (100 ÷ 4):"
curl -s -X POST "${BASE_URL}/api/Calculator/divide?a=100&b=4" -k | jq .

echo ""
echo ""
echo "🌤️  Testing Weather API..."
echo "----------------------------"

echo "1. Query Beijing weather:"
curl -s "${BASE_URL}/api/Weather/北京" -k | jq .

echo ""
echo "2. Query Shanghai weather:"
curl -s "${BASE_URL}/api/Weather/上海" -k | jq .

echo ""
echo ""
echo "✅ All tests completed!"

