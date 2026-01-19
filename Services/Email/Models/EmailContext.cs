using BackendRequisicionPersonal.Models;
using System.Net;

namespace BackendRequisicionPersonal.Services.Email.Models
{
    public class EmailContext
    {
        public SolicitudPersonal Solicitud { get; set; } = null!;
        public string BaseUrl { get; set; } = string.Empty;
        public string EstadoTitulo { get; set; } = string.Empty;
        public string? AprobadorEmail { get; set; }
        public int? NivelAprobador { get; set; }
        public bool AdjuntarPdf { get; set; }
        public bool Aprobado { get; set; }
        public string? MotivoRechazo { get; set; }

        public string GetAprobarUrl(Func<int, string, string?, string> builder)
        {
            return builder(Solicitud.Id, "APROBADA", AprobadorEmail);
        }

        public string GetRechazarUrl(Func<int, string?, int?, string> builder)
        {
            return builder(Solicitud.Id, AprobadorEmail, NivelAprobador);
        }
    }
}
