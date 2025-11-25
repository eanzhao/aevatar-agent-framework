#!/bin/bash

set -e

# Unified benchmark runner for Orleans Stream vs Kafka Stream comparison
# Usage: 
#   ./run-benchmark.sh            # Run both Memory Stream and Kafka Stream tests
#   ./run-benchmark.sh memory     # Run Memory Stream only
#   ./run-benchmark.sh kafka      # Run Kafka Stream only

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

source test-lib.sh

TEST_MODE="${1:-both}"

echo "🚀 Aevatar Agent Framework - Multi-Topic Benchmark"
echo "===================================================="
echo ""

# Cleanup old processes and logs
echo "🧹 Cleaning up old processes..."
bash cleanup.sh
sleep 2

# Check dependencies
check_mongodb

if [ "$TEST_MODE" = "kafka" ] || [ "$TEST_MODE" = "both" ]; then
    check_kafka_running
fi

# Function to run benchmark with specific stream provider
run_benchmark() {
    local provider=$1
    local provider_name=$2
    
    echo ""
    echo "═══════════════════════════════════════════════════════════"
    echo "🔬 Testing: $provider_name"
    echo "═══════════════════════════════════════════════════════════"
    echo ""
    
    # Update appsettings for both Silo and Client
    local silo_settings="src/Aevatar.Silo/appsettings.Development.json"
    local client_settings="src/Aevatar.BusinessServer.HttpApi.Host/appsettings.Development.json"
    
    # Backup original settings
    cp "$silo_settings" "$silo_settings.bak"
    cp "$client_settings" "$client_settings.bak"
    
    # Update Provider setting
    if [[ "$OSTYPE" == "darwin"* ]]; then
        sed -i '' "s/\"Provider\": \".*\"/\"Provider\": \"$provider\"/" "$silo_settings"
        sed -i '' "s/\"Provider\": \".*\"/\"Provider\": \"$provider\"/" "$client_settings"
    else
        sed -i "s/\"Provider\": \".*\"/\"Provider\": \"$provider\"/" "$silo_settings"
        sed -i "s/\"Provider\": \".*\"/\"Provider\": \"$provider\"/" "$client_settings"
    fi
    
    # Start services
    start_silo "Development"
    start_api "Development"
    sleep 15
    
    if ! check_health; then
        echo "❌ Services failed to start for $provider_name!"
        # Restore settings
        mv "$silo_settings.bak" "$silo_settings"
        mv "$client_settings.bak" "$client_settings"
        return 1
    fi
    
    echo "✅ Services started successfully"
    echo ""
    
    # Run benchmark
    echo "🏃 Running benchmark..."
    cd benchmarks/PerformanceTests
    
    local log_name=$(echo "$provider" | tr '[:upper:]' '[:lower:]')
    dotnet run -c Release -- multitopic 2>&1 | tee "../../logs/${log_name}-benchmark.log"
    
    cd ../..
    
    # Cleanup
    echo ""
    echo "🧹 Stopping services..."
    bash cleanup.sh
    sleep 2
    
    # Restore settings
    mv "$silo_settings.bak" "$silo_settings"
    mv "$client_settings.bak" "$client_settings"
    
    echo "✅ $provider_name test completed"
}

# Run tests based on mode
case "$TEST_MODE" in
    memory)
        run_benchmark "OrleansStream" "Orleans Memory Stream"
        ;;
    kafka)
        run_benchmark "Kafka" "Kafka Stream"
        ;;
    both)
        run_benchmark "OrleansStream" "Orleans Memory Stream"
        echo ""
        echo "⏳ Waiting 5 seconds before next test..."
        sleep 5
        run_benchmark "Kafka" "Kafka Stream"
        
        # Generate comparison report
        echo ""
        echo "═══════════════════════════════════════════════════════════"
        echo "📊 Generating Comparison Report"
        echo "═══════════════════════════════════════════════════════════"
        echo ""
        
        if [ -f "logs/orleansstream-benchmark.log" ] && [ -f "logs/kafka-benchmark.log" ]; then
            echo "Memory Stream Results:"
            echo "----------------------"
            grep -A30 "Single-Topic vs Multi-Topic Comparison" logs/orleansstream-benchmark.log | head -35
            
            echo ""
            echo ""
            echo "Kafka Stream Results:"
            echo "---------------------"
            grep -A30 "Single-Topic vs Multi-Topic Comparison" logs/kafka-benchmark.log | head -35
            
            echo ""
            echo "✅ Full logs available at:"
            echo "   - logs/orleansstream-benchmark.log"
            echo "   - logs/kafka-benchmark.log"
        else
            echo "⚠️  Could not find benchmark logs for comparison"
        fi
        ;;
    *)
        echo "❌ Invalid test mode: $TEST_MODE"
        echo "Usage: $0 [memory|kafka|both]"
        exit 1
        ;;
esac

echo ""
echo "🎉 Benchmark completed!"
echo ""

