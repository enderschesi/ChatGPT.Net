using ChatGPT.Net.DTO;
using ChatGPT.Net.Session;
using Newtonsoft.Json;
using SocketIOClient;

namespace ChatGPT.Net;

public class ChatGpt
{
    public List<ChatGptMessage> Messages { get; set; } = new();
    public bool UseCache { get; set; } = true;
    public bool SaveCache { get; set; } = true;
    private bool Ready { get; set; } = false;
    private string BypassNode { get; set; }
    private SocketIO Socket { get; set; }
    private string SessionId { get; set; }
    private List<ChatGptClient> ChatGptClients { get; set; } = new();

    public ChatGpt(ChatGptConfig config = null)
    {
        config ??= new ChatGptConfig();
        SessionId = Guid.NewGuid().ToString();
        UseCache = config.UseCache;
        SaveCache = config.SaveCache;
        BypassNode = config.BypassNode;
        if (SaveCache)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await WaitForReady();
                    var json = JsonConvert.SerializeObject(Messages, Formatting.Indented);
                    await File.WriteAllTextAsync("cache.json", json);
                    await Task.Delay(30000);
                }
            });
        }

        Task.Run(Init);
    }

    public async Task WaitForReady()
    {
        while (!Ready) await Task.Delay(25);
    }

    public bool GetReadyState()
    {
        return Ready;
    }

    public SocketIO GetSocketConnection()
    {
        return Socket;
    }

    private async Task Init()
    {
        Ready = false;
        var firstConnection = true;
        Socket = new SocketIO(BypassNode, new SocketIOOptions
        {
            Reconnection = false,
            Query = new []
            {
                new KeyValuePair<string, string>("client", "csharp"),
                new KeyValuePair<string, string>("version", "1.1.5"),
                new KeyValuePair<string, string>("versionCode", "115"),
                new KeyValuePair<string, string>("signature", SessionId)
            }
        });

        Socket.OnConnected += (sender, e) =>
        {
            firstConnection = false;
            Ready = true;
        };
        
        Socket.OnReconnected += (sender, e) =>
        {
            Ready = true;
        };

        Socket.OnDisconnected += async (sender, e) =>
        {
            if(!Ready) return;
            Ready = false;
            tryAgain:
            try
            {
                await Socket.ConnectAsync();
            }
            catch (Exception ex)
            {
                Ready = false;
                await Task.Delay(5000);
                goto tryAgain;
            }
        };

        Socket.On("serverMessage", Console.WriteLine);

        while (true)
        {
            try
            {
                await Socket.ConnectAsync();
                break;
            }
            catch (Exception e)
            {
                Ready = false;
                await Task.Delay(5000);
            }
        }
    }
    
    public async Task<ChatGptClient> CreateClient(ChatGptClientConfig config)
    {
        var chatGptClient = new ChatGptClient(config, GetReadyState, GetSocketConnection);
        if (string.IsNullOrWhiteSpace(config.SessionToken))
        {
            throw new Exception("You need to provide either a session token or an account.");
        }

        if (!string.IsNullOrWhiteSpace(config.SessionToken))
        {
            await chatGptClient.RefreshAccessToken();
        }

        ChatGptClients.Add(chatGptClient);
        return chatGptClient;
    }
}