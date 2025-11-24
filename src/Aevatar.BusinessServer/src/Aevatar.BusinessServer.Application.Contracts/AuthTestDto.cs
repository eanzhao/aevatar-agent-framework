using System;

namespace Aevatar.BusinessServer;

public class AuthTestDto
{
    public string Message { get; set; }
    public string UserName { get; set; }
    public string UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsAuthenticated { get; set; }
}

