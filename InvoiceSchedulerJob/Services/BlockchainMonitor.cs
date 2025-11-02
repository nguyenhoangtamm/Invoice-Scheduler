using Hangfire;
using InvoiceSchedulerJob.Services.Interfaces;
using Nethereum.Web3;

namespace InvoiceSchedulerJob.Services;

public class BlockchainMonitor : IBlockchainMonitor
{
    private readonly IWeb3 _web3;
    private readonly ILogger<BlockchainMonitor> _logger;

    public BlockchainMonitor(IWeb3 web3, ILogger<BlockchainMonitor> logger)
    {
        _web3 = web3;
        _logger = logger;
    }

    public async Task MonitorLatestBlockAsync()
    {
        try
        {
            var latestBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var blockNumber = (ulong)latestBlock.Value;

            _logger.LogInformation($"Block mới nhất: {blockNumber}");

            // Tạo job xử lý transactions trong block này
            BackgroundJob.Enqueue<IBlockchainMonitor>(x =>
                x.ProcessBlockTransactionsAsync(blockNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi giám sát block mới nhất");
        }
    }

    public async Task ProcessBlockTransactionsAsync(ulong blockNumber)
    {
        try
        {
            _logger.LogInformation($"Đang xử lý block #{blockNumber}");

            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber
                .SendRequestAsync(new Nethereum.Hex.HexTypes.HexBigInteger(blockNumber));

            if (block?.Transactions != null && block.Transactions.Length > 0)
            {
                _logger.LogInformation($"Tìm thấy {block.Transactions.Length} transactions trong block");

                foreach (var tx in block.Transactions)
                {
                    var value = Web3.Convert.FromWei(tx.Value.Value);
                    _logger.LogDebug($"TX: {tx.TransactionHash}");
                    _logger.LogDebug($"  From: {tx.From} To: {tx.To}");
                    _logger.LogDebug($"  Value: {value} ETH");

                    // Xử lý transaction theo nhu cầu của bạn
                    // Ví dụ: Kiểm tra nếu gửi đến địa chỉ của bạn
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi xử lý block {blockNumber}");
        }
    }

    public async Task CheckPendingTransactionsAsync()
    {
        try
        {
            _logger.LogInformation("Đang kiểm tra pending transactions");

            // Thêm logic kiểm tra pending transactions
            // Ví dụ: kiểm tra transactions của các địa chỉ cụ thể

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi kiểm tra pending transactions");
        }
    }
}