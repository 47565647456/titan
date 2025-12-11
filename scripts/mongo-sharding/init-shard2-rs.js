// Initialize Shard 2 Replica Set
// Run via: mongosh --port 27018 < init-shard2-rs.js

rs.initiate({
  _id: "shard2ReplSet",
  members: [
    { _id: 0, host: "mongo-shard2a:27018" },
    { _id: 1, host: "mongo-shard2b:27018" },
    { _id: 2, host: "mongo-shard2c:27018" }
  ]
});
