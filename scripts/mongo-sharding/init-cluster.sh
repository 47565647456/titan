#!/bin/bash
# MongoDB Sharded Cluster Initialization Script
# This script runs once to initialize replica sets and configure sharding

set -e

echo "⏳ Waiting for MongoDB containers to be ready..."
sleep 10

# Function to wait for MongoDB to be ready
wait_for_mongo() {
    local host=$1
    local port=$2
    local max_attempts=30
    local attempt=0
    
    while [ $attempt -lt $max_attempts ]; do
        if mongosh --host "$host" --port "$port" --eval "db.adminCommand('ping')" &>/dev/null; then
            echo "✅ $host:$port is ready"
            return 0
        fi
        attempt=$((attempt + 1))
        echo "⏳ Waiting for $host:$port... (attempt $attempt/$max_attempts)"
        sleep 2
    done
    echo "❌ Timeout waiting for $host:$port"
    return 1
}

# Wait for all MongoDB containers
echo "🔍 Checking MongoDB containers..."
wait_for_mongo "mongo-config1" 27019
wait_for_mongo "mongo-config2" 27019
wait_for_mongo "mongo-config3" 27019
wait_for_mongo "mongo-shard1a" 27018
wait_for_mongo "mongo-shard1b" 27018
wait_for_mongo "mongo-shard1c" 27018
wait_for_mongo "mongo-shard2a" 27018
wait_for_mongo "mongo-shard2b" 27018
wait_for_mongo "mongo-shard2c" 27018

# Initialize Config Server Replica Set
echo "🔧 Initializing Config Server Replica Set..."
mongosh --host mongo-config1 --port 27019 --eval '
rs.initiate({
  _id: "configReplSet",
  configsvr: true,
  members: [
    { _id: 0, host: "mongo-config1:27019" },
    { _id: 1, host: "mongo-config2:27019" },
    { _id: 2, host: "mongo-config3:27019" }
  ]
});
'

echo "⏳ Waiting for config replica set to elect primary..."
sleep 10

# Initialize Shard 1 Replica Set
echo "🔧 Initializing Shard 1 Replica Set..."
mongosh --host mongo-shard1a --port 27018 --eval '
rs.initiate({
  _id: "shard1ReplSet",
  members: [
    { _id: 0, host: "mongo-shard1a:27018" },
    { _id: 1, host: "mongo-shard1b:27018" },
    { _id: 2, host: "mongo-shard1c:27018" }
  ]
});
'

# Initialize Shard 2 Replica Set
echo "🔧 Initializing Shard 2 Replica Set..."
mongosh --host mongo-shard2a --port 27018 --eval '
rs.initiate({
  _id: "shard2ReplSet",
  members: [
    { _id: 0, host: "mongo-shard2a:27018" },
    { _id: 1, host: "mongo-shard2b:27018" },
    { _id: 2, host: "mongo-shard2c:27018" }
  ]
});
'

echo "⏳ Waiting for shard replica sets to elect primaries..."
sleep 15

# Wait for mongos
wait_for_mongo "mongos" 27017

# Add shards to cluster via mongos
echo "🔧 Adding shards to cluster..."
mongosh --host mongos --port 27017 --eval '
sh.addShard("shard1ReplSet/mongo-shard1a:27018,mongo-shard1b:27018,mongo-shard1c:27018");
sh.addShard("shard2ReplSet/mongo-shard2a:27018,mongo-shard2b:27018,mongo-shard2c:27018");
'

# Enable sharding on titan database
echo "🔧 Enabling sharding on titan database..."
mongosh --host mongos --port 27017 --eval '
sh.enableSharding("titan");
sh.shardCollection("titan.OrleansGrainState", { "_id": "hashed" });
'

echo "✅ MongoDB Sharded Cluster initialized successfully!"
echo "📊 Cluster Status:"
mongosh --host mongos --port 27017 --eval 'sh.status()'

# Keep container running briefly to show logs
sleep 5
echo "🏁 Init container complete"
