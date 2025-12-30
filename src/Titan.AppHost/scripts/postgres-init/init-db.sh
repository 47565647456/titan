#!/bin/sh
# PostgreSQL Database Initialization Script
# Waits for PostgreSQL to be ready, then applies initialization SQL scripts

set -e

echo "🔄 Database initialization container starting..."

# Aspire injects connection string as ConnectionStrings__postgres
# Format: Host=postgres;Port=5432;Username=postgres;Password=xxx
# Parse the connection string to extract components
CONNECTION_STRING="${ConnectionStrings__postgres}"

if [ -z "$CONNECTION_STRING" ]; then
    echo "❌ Error: ConnectionStrings__postgres not set"
    exit 1
fi

# Extract components from connection string
PGHOST=$(echo "$CONNECTION_STRING" | sed -n 's/.*Host=\([^;]*\).*/\1/p')
PGPORT=$(echo "$CONNECTION_STRING" | sed -n 's/.*Port=\([^;]*\).*/\1/p')
PGUSER=$(echo "$CONNECTION_STRING" | sed -n 's/.*Username=\([^;]*\).*/\1/p')
PGPASSWORD=$(echo "$CONNECTION_STRING" | sed -n 's/.*Password=\([^;]*\).*/\1/p')

# Use defaults if not found
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}

echo "   Connecting to PostgreSQL at ${PGHOST}:${PGPORT}..."

# Wait for PostgreSQL to accept connections
until PGPASSWORD="$PGPASSWORD" psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -c '\q' 2>/dev/null; do
    echo "   PostgreSQL is not ready yet, waiting..."
    sleep 2
done

echo "✅ PostgreSQL is ready!"

# Apply Orleans schema (creates titan database and tables)
echo "📦 Applying Orleans schema (01-init-orleans.sql)..."
PGPASSWORD="$PGPASSWORD" psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -f /scripts/01-init-orleans.sql

# Apply Admin Identity schema (creates titan-admin database and tables)
echo "📦 Applying Admin Identity schema (02-init-admin.sql)..."
PGPASSWORD="$PGPASSWORD" psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -f /scripts/02-init-admin.sql

echo "✅ Database initialization complete!"
