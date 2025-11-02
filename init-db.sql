# Create databases
CREATE DATABASE invoice_system;

CREATE DATABASE hangfire_jobs;

# Grant permissions
GRANT ALL PRIVILEGES ON DATABASE invoice_system TO postgres;

GRANT ALL PRIVILEGES ON DATABASE hangfire_jobs TO postgres;