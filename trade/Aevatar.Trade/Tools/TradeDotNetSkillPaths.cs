namespace Aevatar.Trade.Tools;

public static class TradeDotNetSkillPaths
{
    public static string WeexGetBalances => Resolve("weex_get_balances.cs");
    public static string WeexGetOrder => Resolve("weex_get_order.cs");
    public static string WeexGetOpenOrders => Resolve("weex_get_open_orders.cs");
    public static string WeexPlaceOrder => Resolve("weex_place_order.cs");
    public static string WeexCancelOrder => Resolve("weex_cancel_order.cs");

    private static string Resolve(string fileName)
    {
        // Primary: copied to output directory by csproj (Tools/DotNetSkills -> <bin>/DotNetSkills)
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Tools", "DotNetSkills", fileName);
        if (File.Exists(fromOutput))
            return fromOutput;

        // Backward/alternative layout: <bin>/DotNetSkills/<file>
        var fromOutputFlat = Path.Combine(AppContext.BaseDirectory, "DotNetSkills", fileName);
        if (File.Exists(fromOutputFlat))
            return fromOutputFlat;

        // Fallback: repo-relative (useful when running from repo root)
        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), "trade", "Aevatar.Trade", "Tools", "DotNetSkills", fileName);
        if (File.Exists(fromCwd))
            return fromCwd;

        // Last resort: relative to this source file location is not available at runtime.
        return fromOutput;
    }
}


