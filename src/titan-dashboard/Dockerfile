# Multi-stage Dockerfile for titan-dashboard
# Stage 1: Build the Vite/React application
# Stage 2: Serve with Nginx

# ==============================================================================
# Build Stage
# ==============================================================================
FROM node:22-alpine AS build

WORKDIR /app

# Copy package files first for better layer caching
COPY package.json package-lock.json ./

# Install dependencies
RUN npm ci

# Copy source files
COPY . .

# Build the application
# VITE_API_URL is left empty so the app uses relative URLs (gateway handles routing)
ENV VITE_API_URL=""
RUN npm run build

# ==============================================================================
# Production Stage
# ==============================================================================
FROM nginx:alpine AS production

# Copy custom nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf

# Copy built files from build stage
# Vite outputs to 'dist' directory by default
COPY --from=build /app/dist /usr/share/nginx/html/dashboard

# Security: Run nginx as non-root user
# Create writable directories for nginx runtime files
RUN mkdir -p /var/cache/nginx /var/run/nginx && \
    chown -R nginx:nginx /var/cache/nginx /var/run/nginx && \
    chown -R nginx:nginx /usr/share/nginx/html/dashboard && \
    # Ensure nginx can write to log directories  
    chown -R nginx:nginx /var/log/nginx

# Nginx runs on port 80 (requires binding to 8080 for non-root, but nginx alpine allows 80)
EXPOSE 80

# Health check for container orchestrators
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost/health || exit 1

# Switch to non-root user
USER nginx

# Start nginx in foreground
CMD ["nginx", "-g", "daemon off;"]
