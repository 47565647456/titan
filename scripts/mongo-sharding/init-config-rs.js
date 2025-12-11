// MongoDB Sharded Cluster Initialization Script
// Run via: mongosh < init-config-rs.js

// Initialize Config Server Replica Set
rs.initiate({
  _id: "configReplSet",
  configsvr: true,
  members: [
    { _id: 0, host: "mongo-config1:27019" },
    { _id: 1, host: "mongo-config2:27019" },
    { _id: 2, host: "mongo-config3:27019" }
  ]
});
