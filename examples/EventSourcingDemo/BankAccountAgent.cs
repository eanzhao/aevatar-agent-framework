using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using EventSourcingDemo.Events;
using Microsoft.Extensions.Logging;
using Demo.Agents;

namespace EventSourcingDemo;

/// <summary>
/// 支持 EventSourcing 的银行账户 Agent
/// 使用 Demo.Agents 中定义的 BankAccountState
/// </summary>
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
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
        return Task.FromResult($"Bank Account Agent for {State.AccountHolder}");
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
        if (State.Balance < (double)amount)
        {
            throw new InvalidOperationException(
                $"Insufficient balance. Current: ${State.Balance}, Requested: ${amount}");
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
                State.AccountHolder = created.AccountHolder;
                State.Balance = created.InitialBalance;
                State.History.Add($"[{DateTime.UtcNow:HH:mm:ss}] Account created for {created.AccountHolder}");
                break;

            case MoneyDeposited deposited:
                State.Balance += deposited.Amount;
                State.TransactionCount++;
                State.History.Add(
                    $"[{State.TransactionCount}] Deposited ${deposited.Amount:F2} - {deposited.Description}");
                break;

            case MoneyWithdrawn withdrawn:
                State.Balance -= withdrawn.Amount;
                State.TransactionCount++;
                State.History.Add(
                    $"[{State.TransactionCount}] Withdrew ${withdrawn.Amount:F2} - {withdrawn.Description}");
                break;
        }

        return Task.CompletedTask;
    }
}