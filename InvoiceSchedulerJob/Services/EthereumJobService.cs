using InvoiceSchedulerJob.Services.Interfaces;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using InvoiceSchedulerJob.DTOs;

namespace InvoiceSchedulerJob.Services;

public class EthereumJobService : IEthereumJobService
{
    private readonly IWeb3 _web3;
    private readonly ILogger<EthereumJobService> _logger;
    private readonly IConfiguration _configuration;

    public EthereumJobService(
        IWeb3 web3,
        ILogger<EthereumJobService> logger,
        IConfiguration configuration)
    {
        _web3 = web3;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string> SendTransactionAsync(string toAddress, decimal amount)
    {
        try
        {
            _logger.LogInformation($"Bắt đầu gửi {amount} ETH đến {toAddress}");

            var fromAddress = _configuration["Ethereum:WalletAddress"];
            var privateKey = _configuration["Ethereum:PrivateKey"];

            // Chuyển đổi ETH sang Wei
            var amountInWei = Web3.Convert.ToWei(amount);

            // Lấy account từ private key
            var account = new Nethereum.Web3.Accounts.Account(privateKey);
            var web3 = new Web3(account, _configuration["Ethereum:RpcUrl"]);

            // Gửi transaction
            var txHash = await web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(toAddress, amount);

            _logger.LogInformation($"Transaction đã gửi thành công: {txHash.TransactionHash}");
            return txHash.TransactionHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi gửi transaction đến {toAddress}");
            throw;
        }
    }

    public async Task<string> GetBalanceAsync(string address)
    {
        try
        {
            _logger.LogInformation($"Đang kiểm tra số dư của địa chỉ: {address}");

            var balance = await _web3.Eth.GetBalance.SendRequestAsync(address);
            var etherAmount = Web3.Convert.FromWei(balance.Value);

            _logger.LogInformation($"Số dư: {etherAmount} ETH");
            return etherAmount.ToString("F4");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi lấy số dư cho địa chỉ {address}");
            throw;
        }
    }

    public async Task<TransactionReceiptDto> WaitForTransactionReceiptAsync(string transactionHash)
    {
        try
        {
            _logger.LogInformation($"Đang chờ xác nhận transaction: {transactionHash}");

            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt
                .SendRequestAsync(transactionHash);

            var attempts = 0;
            while (receipt == null && attempts < 60) // Chờ tối đa 2 phút
            {
                await Task.Delay(2000);
                receipt = await _web3.Eth.Transactions.GetTransactionReceipt
                    .SendRequestAsync(transactionHash);
                attempts++;
            }

            if (receipt != null)
            {
                var status = receipt.Status.Value == 1 ? "Thành công" : "Thất bại";
                _logger.LogInformation($"Transaction {status} tại block: {receipt.BlockNumber}");

                return new TransactionReceiptDto
                {
                    TransactionHash = receipt.TransactionHash,
                    BlockNumber = receipt.BlockNumber.ToString(),
                    Status = status,
                    GasUsed = receipt.GasUsed.ToString()
                };
            }

            throw new Exception("Không nhận được receipt sau thời gian chờ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi chờ receipt cho {transactionHash}");
            throw;
        }
    }

    public async Task<string> CallSmartContractReadAsync(
        string contractAddress,
        string abi,
        string functionName,
        params object[] parameters)
    {
        try
        {
            _logger.LogInformation($"Đang gọi function {functionName} từ contract {contractAddress}");

            var contract = _web3.Eth.GetContract(abi, contractAddress);
            var function = contract.GetFunction(functionName);
            var result = await function.CallAsync<string>(parameters);

            _logger.LogInformation($"Kết quả: {result}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi gọi function {functionName}");
            throw;
        }
    }

    public async Task<string> CallSmartContractWriteAsync(
        string contractAddress,
        string abi,
        string functionName,
        params object[] parameters)
    {
        try
        {
            _logger.LogInformation($"Đang thực thi function {functionName} trên contract {contractAddress}");

            var privateKey = _configuration["Ethereum:PrivateKey"];
            var account = new Nethereum.Web3.Accounts.Account(privateKey);
            var web3 = new Web3(account, _configuration["Ethereum:RpcUrl"]);

            var contract = web3.Eth.GetContract(abi, contractAddress);
            var function = contract.GetFunction(functionName);

            var txHash = await function.SendTransactionAsync(
                account.Address,
                parameters);

            _logger.LogInformation($"Transaction gửi thành công: {txHash}");
            return txHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Lỗi khi thực thi function {functionName}");
            throw;
        }
    }
}