# Dockerfile for Shadowing 3x Tutor Backend
# Multi-stage build for minimal production image

# ============================================
# Stage 1: Build dependencies
# ============================================
FROM node:20-alpine AS builder

WORKDIR /app

# Copy package files first (better caching)
COPY package*.json ./

# Install all dependencies (including dev for potential build steps)
RUN npm ci --only=production

# ============================================
# Stage 2: Production image
# ============================================
FROM node:20-alpine AS production

# Security: Run as non-root user
RUN addgroup -g 1001 -S nodejs && \
    adduser -S nodejs -u 1001

WORKDIR /app

# Copy dependencies from builder
COPY --from=builder /app/node_modules ./node_modules

# Copy application code
COPY package*.json ./
COPY index.js ./
COPY src/ ./src/
COPY data/ ./data/

# Create cache directory with proper permissions
RUN mkdir -p /app/cache/tts /app/cache/stt && \
    chown -R nodejs:nodejs /app

# Switch to non-root user
USER nodejs

# Environment variables (can be overridden at runtime)
ENV NODE_ENV=production
ENV PORT=3000
ENV HOST=0.0.0.0

# Expose port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:3000/api/health || exit 1

# Start the server
CMD ["node", "index.js"]
