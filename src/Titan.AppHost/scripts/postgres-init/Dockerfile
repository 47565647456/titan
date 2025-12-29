# PostgreSQL Database Initialization Container
# Runs init scripts after PostgreSQL is ready

FROM postgres:17-alpine

# Copy initialization scripts (must be in build context)
COPY 01-init-orleans.sql /scripts/
COPY 02-init-admin.sql /scripts/
COPY init-db.sh /scripts/

# Make script executable
RUN chmod +x /scripts/init-db.sh

# Run the init script
ENTRYPOINT ["/scripts/init-db.sh"]
