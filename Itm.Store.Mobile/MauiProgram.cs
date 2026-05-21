using Itm.Store.Mobile.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Itm.Store.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });


        // Registro de Arquitectura Nivel 5: Inyección de Dependencias

        // 1. Registramos nuestro "Peaje" de seguridad (AuthHandler) para que se ejecute en cada petición HTTP.
        builder.Services.AddTransient<AuthHandler>();

        // 2. Registramos el cliente HTTP apuntando al API Gateway via Ingress HTTPS.
        // En produccion, el Ingress expone el Gateway mediante TLS en api.itm-tickets.com.
        // Para desarrollo local con emulador Android, usar 10.0.2.2 (localhost del emulador).
        builder.Services.AddHttpClient("GatewayClient", client =>
        {
            client.BaseAddress = new Uri("https://api.itm-tickets.com");
            client.DefaultRequestHeaders.Add("X-Gateway-Source", "ItmStoreMobile");
        })
        .AddHttpMessageHandler<AuthHandler>();

        // 3. Registramos la vista principal de la aplicación (MainPage) para que se muestre al iniciar la app.
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
