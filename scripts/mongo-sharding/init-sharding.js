// Add Shards and Enable Sharding on Titan Database
// Run via: mongosh --port 27017 < init-sharding.js (on mongos)

// Add shards to the cluster
sh.addShard("shard1ReplSet/mongo-shard1a:27018,mongo-shard1b:27018,mongo-shard1c:27018");
sh.addShard("shard2ReplSet/mongo-shard2a:27018,mongo-shard2b:27018,mongo-shard2c:27018");

// Enable sharding on titan database
sh.enableSharding("titan");

// Shard Orleans collections using hashed _id for even distribution
// OrleansGrainState collection
sh.shardCollection("titan.OrleansGrainState", { "_id": "hashed" });

// Note: Additional Orleans collections will be auto-created and can be sharded later
print("Sharding initialized successfully!");
print("Status:");
sh.status();
