using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Itm.Store.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IHttpClientFactory _httpClientFactory;
    private HubConnection? _hubConnection;

    public MainPage(IHttpClientFactory httpClientFactory)
    {
        InitializeComponent();
        _httpClientFactory = httpClientFactory;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ConnectToSignalR("admin@itm.edu.co");
    }

    private async Task ConnectToSignalR(string email)
    {
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://10.0.2.2:5400/hubs/tickets")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<string>("ReceiveTicket", async (ticketJson) =>
            {
                await Dispatcher.DispatchAsync(() =>
                {
                    ResultLabel.Text = $"Ticket recibido:\n{ticketJson}";
                    ResultLabel.TextColor = Colors.Green;

                    if (!string.IsNullOrEmpty(ticketJson) && ticketJson.Length > 100)
                    {
                        QrCodeImage.Source = ImageSource.FromStream(() =>
                            new MemoryStream(Convert.FromBase64String(ticketJson)));
                        QrCodeImage.IsVisible = true;
                    }
                });
            });

            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinGroup", email);
        }
        catch (Exception ex)
        {
            await Dispatcher.DispatchAsync(() =>
            {
                ResultLabel.Text = $"SignalR error: {ex.Message}";
                ResultLabel.TextColor = Colors.Red;
            });
        }
    }

    //private async void OnLoginClicked(object sender, EventArgs e)
    //{
    //    string simulatedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJJdG1JZGVudGl0eVNlcnZlciIsImF1ZCI6Ikl0bVN0b3JlQXBpcyIsImVtYWlsIjoiYWRtaW5AaXRtLmVkdS5jbyIsInJvbGUiOiJBZG1pbmlzdHJhZG9yIn0.PaSdxe8NkHzbkrTA40janIgKn4gnVp63yWh_cenvUDw";

    //    await SecureStorage.Default.SetAsync("jwt_token", simulatedToken);

    //    ResultLabel.Text = "Token JWT guardado seguro en el dispositivo!";
    //    ResultLabel.TextColor = Colors.Green;
    //}
    private async void OnLoginClicked(object sender, EventArgs e)
    {
        try
        {
            var email = EmailEntry.Text?.Trim();
            var password = PasswordEntry.Text;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ResultLabel.Text = "Debes ingresar correo y contraseña.";
                ResultLabel.TextColor = Colors.Red;
                return;
            }

            await SecureStorage.Default.SetAsync("jwt_token", "TOKEN_TEMPORAL_DE_PRUEBA");

            ResultLabel.Text = $"Sesión iniciada: {email}";
            ResultLabel.TextColor = Colors.LightGreen;
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"Error login: {ex.Message}";
            ResultLabel.TextColor = Colors.Red;
        }
    }

    private async void OnGetDataClicked(object sender, EventArgs e)
    {
        try
        {
            ResultLabel.Text = "Consultando Gateway...";
            ResultLabel.TextColor = Colors.Orange;

            var client = _httpClientFactory.CreateClient("GatewayClient");
            var response = await client.GetAsync("/api/products/1/check-stock");

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                ResultLabel.Text = $"EXITO:\n{data}";
                ResultLabel.TextColor = Colors.Green;
            }
            else
            {
                ResultLabel.Text = $"ERROR {response.StatusCode}:\n{await response.Content.ReadAsStringAsync()}";
                ResultLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"ERROR DE RED:\n{ex.Message}";
            ResultLabel.TextColor = Colors.Red;
        }
    }

    private async void OnGetPriceClicked(object sender, EventArgs e)
    {
        try
        {
            ResultLabel.Text = "Consultando precio del evento...";
            ResultLabel.TextColor = Colors.Orange;

            var client = _httpClientFactory.CreateClient("GatewayClient");
            var response = await client.GetAsync("/bodega/prices/1");

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                ResultLabel.Text = $"Precio:\n{data}";
                ResultLabel.TextColor = Colors.Green;
            }
            else
            {
                ResultLabel.Text = $"ERROR {response.StatusCode}:\n{await response.Content.ReadAsStringAsync()}";
                ResultLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"ERROR DE RED:\n{ex.Message}";
            ResultLabel.TextColor = Colors.Red;
        }
    }

    private async void OnBuyTicketClicked(object sender, EventArgs e)
    {
        try
        {
            ResultLabel.Text = "Comprando boleta...";
            ResultLabel.TextColor = Colors.Orange;

            var client = _httpClientFactory.CreateClient("GatewayClient");
            var body = new { productId = 1, quantity = 1 };
            var response = await client.PostAsJsonAsync("/api/orders", body);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                ResultLabel.Text = $"Compra exitosa:\n{data}";
                ResultLabel.TextColor = Colors.Green;
            }
            else
            {
                ResultLabel.Text = $"ERROR {response.StatusCode}:\n{await response.Content.ReadAsStringAsync()}";
                ResultLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"ERROR DE RED:\n{ex.Message}";
            ResultLabel.TextColor = Colors.Red;
        }
    }
}
