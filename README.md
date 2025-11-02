# Invoice Scheduler Job System

A comprehensive background job system for processing invoices through IPFS storage, batching, and blockchain publishing using Hangfire, Entity Framework, and Ethereum integration.

## Overview

This system implements three main resilient scheduled background jobs:

1. **UploadToIpfsJob** - Uploads invoices to IPFS using Pinata
2. **CreateBatchJob** - Groups invoices into batches with Merkle tree proofs
3. **SubmitToBlockchainJob** - Publishes batches to Ethereum Sepolia blockchain

## Architecture

### Components

-   **Database Models**: Invoice, InvoiceBatch, InvoiceLine with EF Core
-   **Job Services**: Three main job implementations with retry logic and error handling
-   **IPFS Service**: Pinata integration with rate limiting and retry policies
-   **Blockchain Service**: Ethereum integration with gas estimation and transaction monitoring
-   **Merkle Tree Service**: Deterministic tree construction with proof generation
-   **Metrics Service**: Observability and monitoring
-   **REST API**: Manual job triggering and management endpoints

### Technology Stack

-   **.NET 9** - Platform
-   **Hangfire** - Background job processing
-   **Entity Framework Core** - Database ORM
-   **PostgreSQL** - Database
-   **Nethereum** - Ethereum integration
-   **Polly** - Retry policies and resilience
-   **ASP.NET Core** - Web API for management

## Configuration

### Environment Variables

Create `appsettings.json` or use environment variables:

```json
{
    "ConnectionStrings": {
        "DefaultConnection": "Host=localhost;Port=5432;Database=invoice_system;Username=postgres;Password=yourpassword",
        "HangfireConnection": "Host=localhost;Port=5432;Database=hangfire_jobs;Username=postgres;Password=yourpassword"
    },
    "Ipfs": {
        "ApiKey": "YOUR_PINATA_API_KEY",
        "ApiSecret": "YOUR_PINATA_API_SECRET",
        "BaseUrl": "https://api.pinata.cloud",
        "MaxRetries": 3,
        "RetryDelayMs": 1000,
        "RateLimitPerMinute": 20
    },
    "Blockchain": {
        "RpcUrl": "https://rpc.ankr.com/eth_sepolia",
        "ChainId": "11155111",
        "PrivateKey": "YOUR_SEPOLIA_PRIVATE_KEY",
        "ContractAddress": "0xYourContractAddress",
        "MaxRetries": 3,
        "ConfirmationBlocks": 3,
        "MaxGasPrice": 50000000000
    },
    "Jobs": {
        "BatchSize": 100,
        "UploadCron": "*/5 * * * *",
        "BatchCron": "*/15 * * * *",
        "BlockchainCron": "*/10 * * * *",
        "ConcurrentUploads": 5,
        "DryRunMode": false
    }
}
```

### Required Setup

1. **PostgreSQL Database**: Two databases needed

    - `invoice_system` - Main application data
    - `hangfire_jobs` - Hangfire job storage

2. **Pinata Account**:

    - Sign up at [pinata.cloud](https://pinata.cloud)
    - Get API key and secret from dashboard

3. **Ethereum Sepolia**:
    - Get Sepolia ETH from faucets
    - Deploy smart contract with `submitBatch` and `submitInvoice` functions
    - Configure private key (use environment variables in production)

## Installation & Running

### Prerequisites

```bash
# Install .NET 9 SDK
# Install PostgreSQL
# Create databases
createdb invoice_system
createdb hangfire_jobs
```

### Build & Run

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run (will create database tables automatically)
dotnet run
```

### Endpoints

-   `http://localhost:5000/hangfire` - Hangfire Dashboard
-   `http://localhost:5000/api/jobs/` - Job management API

## Usage

### Automatic Operation

Jobs run automatically based on cron schedules:

-   **Upload to IPFS**: Every 5 minutes
-   **Create Batch**: Every 15 minutes
-   **Submit to Blockchain**: Every 10 minutes

### Manual Triggering

Use the REST API to manually trigger jobs:

```bash
# Trigger IPFS upload (dry run)
curl -X POST "http://localhost:5000/api/jobs/upload-to-ipfs/trigger?dryRun=true"

# Force create batch
curl -X POST "http://localhost:5000/api/jobs/create-batch/trigger?forceRun=true"

# Submit to blockchain
curl -X POST "http://localhost:5000/api/jobs/submit-to-blockchain/trigger"
```

### Database Operations

Insert test invoices:

```sql
INSERT INTO invoices (
    invoice_number, form_number, serial, issued_organization_id, issued_by_user_id,
    seller_name, seller_tax_id, customer_name, customer_tax_id,
    status, issue_date, sub_total, tax_amount, total_amount, currency
) VALUES (
    'INV-001', 'FORM-001', 'SER-001', 1, 1,
    'Test Seller', '1234567890', 'Test Customer', '0987654321',
    1, NOW(), 100.00, 10.00, 110.00, 'VND'
);
```

## Job Flow

### 1. Upload to IPFS Job

1. **Query**: Find invoices with `status = 1` (UPLOADED) and empty CID
2. **Process**:
    - Create canonical JSON representation
    - Upload to Pinata IPFS
    - Compute CID hash and immutable hash
    - Update invoice with CID, hashes, and `status = 2` (IPFS_STORED)
3. **Error Handling**: Mark failed invoices with `status = 3` (IPFS_FAILED)

### 2. Create Batch Job

1. **Query**: Find invoices with `status = 2` (IPFS_STORED)
2. **Process**:
    - Group invoices by batch size (configurable)
    - Create InvoiceBatch record
    - Build Merkle tree from CIDs
    - Generate proofs for each invoice
    - Upload batch metadata to IPFS
    - Update batch with Merkle root and batch CID
    - Set invoices to `status = 5` (BLOCKCHAIN_PENDING)

### 3. Submit to Blockchain Job

1. **Query**: Find batches with `status = "ready_to_send"`
2. **Process**:
    - Submit batch to smart contract with Merkle root
    - Monitor transaction status
    - Update on confirmation with block number
    - Set status to `BLOCKCHAIN_CONFIRMED` or `BLOCKCHAIN_FAILED`

## Smart Contract Interface

Required contract functions:

```solidity
function submitBatch(
    bytes32 merkleRoot,
    string memory batchId,
    string memory metadataCid
) external;

function submitInvoice(
    bytes32 cidHash,
    uint256 invoiceId
) external;
```

## Monitoring & Observability

### Hangfire Dashboard

-   View job status and history
-   Retry failed jobs
-   Monitor queues and workers

### Logs

Structured logging with different levels:

-   **Information**: Job execution, success/failure
-   **Debug**: Detailed processing steps
-   **Warning**: Retries, rate limits
-   **Error**: Failures with stack traces

### Metrics

Built-in metrics tracking:

-   Job execution counts and duration
-   Success/failure rates
-   Invoice processing throughput
-   Batch creation rates
-   Blockchain submission status

## Error Handling & Resilience

### Retry Policies

-   **IPFS**: Exponential backoff with jitter
-   **Blockchain**: Configurable retries with gas price adjustment
-   **Database**: Optimistic locking to prevent double-processing

### Rate Limiting

-   **IPFS**: Semaphore-based rate limiting (20/minute default)
-   **Blockchain**: Delays between submissions

### Transaction Safety

-   Database transactions for atomic updates
-   Optimistic locking using SELECT FOR UPDATE
-   Compensation logic for partial failures

## Development

### Testing

```bash
# Run with dry run mode
curl -X POST "http://localhost:5000/api/jobs/upload-to-ipfs/trigger?dryRun=true"

# Check database state
SELECT id, invoice_number, status, cid, batch_id FROM invoices ORDER BY id DESC LIMIT 10;
SELECT id, batch_id, count, merkle_root, status FROM invoice_batches ORDER BY id DESC;
```

### Adding New Jobs

1. Create interface in `Services/Interfaces/`
2. Implement service in `Services/`
3. Register in `Program.cs`
4. Add recurring job configuration
5. Add API endpoint in `JobsController`

## Security Considerations

### Production Deployment

1. **Private Keys**: Use Azure Key Vault or AWS KMS
2. **Database**: Use connection strings with minimal permissions
3. **API**: Add authentication and authorization
4. **IPFS**: Validate uploaded content
5. **Monitoring**: Set up alerts for failed jobs

### Environment Variables

```bash
export IPFS_API_KEY="your_pinata_key"
export BLOCKCHAIN_PRIVATE_KEY="your_private_key"
export DATABASE_CONNECTION="your_connection_string"
```

## Troubleshooting

### Common Issues

1. **Database Connection**: Check PostgreSQL is running and accessible
2. **IPFS Upload Fails**: Verify Pinata API credentials
3. **Blockchain Submission Fails**: Check Sepolia RPC URL and gas settings
4. **Jobs Not Running**: Verify Hangfire is configured correctly

### Log Analysis

```bash
# View recent logs
docker logs invoice-scheduler-job --tail 100

# Filter by job type
grep "UploadToIpfsJob" logs/app.log

# Check error patterns
grep "ERROR\|FAILED" logs/app.log
```

## License

MIT License - see LICENSE file for details.
