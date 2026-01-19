using BackendRequisicionPersonal.Models;

namespace BackendRequisicionPersonal.Services.Email
{
    public interface IEmailService
    {
        Task EnviarCorreoEstadoSolicitanteAsync(SolicitudPersonal solicitud, string estadoTitulo);
        Task EnviarCorreoFinalSolicitanteAsync(SolicitudPersonal solicitud, bool aprobado, string? motivoRechazo = null);
        Task EnviarCorreoGhConBotonesAsync(SolicitudPersonal solicitud, IEnumerable<string> destinatariosGh, string estadoTitulo);
        Task EnviarCorreoAprobadorAsync(SolicitudPersonal solicitud, string aprobadorEmail);
        Task EnviarCorreoVpGhConBotonesAsync(SolicitudPersonal solicitud, IEnumerable<string> destinatariosVpGh);
        Task EnviarCorreoCierreANominaYGhAsync(SolicitudPersonal solicitud);
        Task NotificarSolicitanteSinDuplicarAprobadoresAsync(SolicitudPersonal solicitud, string estadoTitulo, IEnumerable<string> aprobadoresRaw);
    }
}
