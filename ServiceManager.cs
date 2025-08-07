using System.ServiceProcess;
using System.Collections.Generic;
using WinFastGUI.Model;

public static class ServiceManager
{
    public static List<ServiceInfo> GetAllServices()
    {
        var services = new List<ServiceInfo>();
        foreach (var sc in ServiceController.GetServices())
        {
            services.Add(new ServiceInfo
            {
                ServiceName = sc.ServiceName,
                DisplayName = sc.DisplayName,
                // Kendi filtrelerin ve description ekleyebilirsin
                IsRecommendedStoppable = RecommendedServices.Any(x => x.ServiceName == sc.ServiceName),
                Note = RecommendedServices.FirstOrDefault(x => x.ServiceName == sc.ServiceName)?.Note ?? ""
            });
        }
        return services;
    }

    // Senin verdiğin listedeki servislerin kod karşılıkları:
    public static List<ServiceInfo> RecommendedServices = new()
    {
        new ServiceInfo { ServiceName = "BthServ", DisplayName = "Bluetooth", Note = "Bluetooth ve bağlı cihazlar" },
        new ServiceInfo { ServiceName = "PhoneSvc", DisplayName = "Telefon", Note = "Bluetooth ihtiyacı" },
        new ServiceInfo { ServiceName = "Spooler", DisplayName = "Yazıcı", Note = "Yazıcı hizmeti" },
        new ServiceInfo { ServiceName = "WiaRpc", DisplayName = "Tarayıcı, Kamera", Note = "OBS ihtiyacı" },
        new ServiceInfo { ServiceName = "PenService", DisplayName = "Kalem", Note = "Kalem ve dokunmatik" },
        // ... (Diğerlerini de ekle)
    };

    public static void StopServices(IEnumerable<string> serviceNames)
    {
        foreach (var name in serviceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running)
                    sc.Stop();
            }
            catch { /* Hata yakalama, loglayabilirsin */ }
        }
    }
}
