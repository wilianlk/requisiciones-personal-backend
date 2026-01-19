using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;

namespace BackendRequisicionPersonal.Helpers
{
    public static class UrlHelper
    {
        /// <summary>
        /// Construye URL para endpoint de acción de aprobación
        /// </summary>
        public static string BuildAccionUrl(HttpRequest request, int id, string estado, string? actorEmail = null)
        {
            var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
            var url = $"{baseUrl}/api/aprobaciones/accion?id={id}&estado={Uri.EscapeDataString(estado)}";
            
            if (!string.IsNullOrWhiteSpace(actorEmail))
                url += $"&actorEmail={Uri.EscapeDataString(actorEmail)}";
            
            return url;
        }

        /// <summary>
        /// Construye URL para el frontend de rechazo
        /// </summary>
        public static string BuildFrontRechazoUrl(
            IConfiguration config, 
            HttpContext? context, 
            int id, 
            string? actorEmail = null, 
            int? nivel = null)
        {
            var baseUrl = config["Frontend:BaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var req = context?.Request;
                baseUrl = req != null ? $"{req.Scheme}://{req.Host}" : "";
            }

            var url = $"{baseUrl}/rechazar?id={id}";
            
            if (!string.IsNullOrWhiteSpace(actorEmail))
                url += $"&actorEmail={Uri.EscapeDataString(actorEmail)}";
            
            if (nivel.HasValue && nivel > 0)
                url += $"&nivel={nivel.Value}";

            return url;
        }

        /// <summary>
        /// Construye URL completa para un path relativo
        /// </summary>
        public static string BuildGetUrl(HttpRequest request, string relativePathAndQuery)
        {
            var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
            return $"{baseUrl}{relativePathAndQuery}";
        }

        public static string BuildFrontGestionSeleccionUrl(IConfiguration config, HttpContext? context, int id)
        {
            var baseUrl = config["Frontend:BaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var req = context?.Request;
                baseUrl = req != null ? $"{req.Scheme}://{req.Host}" : "";
            }

            return $"{baseUrl}/seleccionado?id={id}";
        }
    }
}
