// Initialize Shard 1 Replica Set
// Run via: mongosh --port 27018 < init-shard1-rs.js

rs.initiate({
  _id: "shard1ReplSet",
  members: [
    { _id: 0, host: "mongo-shard1a:27018" },
    { _id: 1, host: "mongo-shard1b:27018" },
    { _id: 2, host: "mongo-shard1c:27018" }
  ]
});
