using LojaVirtual.Models;



namespace LojaVirtual.Web.Services;

public interface IMercadoPagoService
{
    Task<string> CriarPreferenciaAsync(Order order);
}