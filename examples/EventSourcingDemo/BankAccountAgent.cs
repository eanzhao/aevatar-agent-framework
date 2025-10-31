using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using EventSourcingDemo.Events;
using Microsoft.Extensions.Logging;

namespace EventSourcingDemo;

/// <summary>
/// 银行账户状态
/// </summary>
public class BankAccountState
{
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
    public string AccountHolder { get; set; } = "Anonymous";
    public List<string> History { get; set; } = new();
}

/// <summary>
/// 支持 EventSourcing 的银行账户 Agent
/// </summary>
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    // 无参数构造函数（用于工厂创建）
    public BankAccountAgent() : base(Guid.NewGuid(), null, null)
    {
    }
    
    // 完整构造函数
    public BankAccountAgent(
        Guid id,
        IEventStore? eventStore = null,
        ILogger<BankAccountAgent>? logger = null)
        : base(id, eventStore, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Bank Account Agent for {_state.AccountHolder}");
    }
    
    /// <summary>
    /// 创建账户
    /// </summary>
    public async Task CreateAccountAsync(string accountHolder, decimal initialBalance = 0)
    {
        var evt = new AccountCreated 
        { 
            AccountHolder = accountHolder,
            InitialBalance = (double)initialBalance
        };
        await RaiseStateChangeEventAsync(evt);
    }
    
    /// <summary>
    /// 存款
    /// </summary>
    public async Task DepositAsync(decimal amount, string description = "")
    {
        var evt = new MoneyDeposited 
        { 
            Amount = (double)amount,
            Description = description ?? $"Deposit at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
        };
        await RaiseStateChangeEventAsync(evt);
    }
    
    /// <summary>
    /// 取款
    /// </summary>
    public async Task WithdrawAsync(decimal amount, string description = "")
    {
        if (_state.Balance < amount)
        {
            throw new InvalidOperationException($"Insufficient balance. Current: ${_state.Balance}, Requested: ${amount}");
        }
        
        var evt = new MoneyWithdrawn 
        { 
            Amount = (double)amount,
            Description = description ?? $"Withdrawal at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
        };
        await RaiseStateChangeEventAsync(evt);
    }
    
    /// <summary>
    /// 应用状态变更事件
    /// </summary>
    protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        switch (evt)
        {
            case AccountCreated created:
                _state.AccountHolder = created.AccountHolder;
                _state.Balance = (decimal)created.InitialBalance;
                _state.History.Add($"[{DateTime.UtcNow:HH:mm:ss}] Account created for {created.AccountHolder}");
                break;
                
            case MoneyDeposited deposited:
                _state.Balance += (decimal)deposited.Amount;
                _state.TransactionCount++;
                _state.History.Add($"[{_state.TransactionCount}] Deposited ${deposited.Amount:F2} - {deposited.Description}");
                break;
            
            case MoneyWithdrawn withdrawn:
                _state.Balance -= (decimal)withdrawn.Amount;
                _state.TransactionCount++;
                _state.History.Add($"[{_state.TransactionCount}] Withdrew ${withdrawn.Amount:F2} - {withdrawn.Description}");
                break;
        }
        
        return Task.CompletedTask;
    }
}