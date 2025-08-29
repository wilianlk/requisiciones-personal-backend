using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Models.Settings;
using BackendRequisicionPersonal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendRequisicionPersonal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RequisicionesController : ControllerBase
    {
        private readonly SolicitudesPersonalService _service;
        private readonly ILogger<RequisicionesController> _logger;
        private readonly SmtpSettings _smtp;
        private readonly IConfiguration _config;

        private const string CC_POWERAPPS = "powerapps@recamier.com";

        public RequisicionesController(
            SolicitudesPersonalService service,
            ILogger<RequisicionesController> logger,
            IOptions<SmtpSettings> smtpOptions,
            IConfiguration config)
        {
            _service = service;
            _logger = logger;
            _smtp = smtpOptions.Value;
            _config = config;
        }

        /* ===========================================================
         *  Health
         * =========================================================== */
        [HttpGet("test-connection")]
        public IActionResult TestConnection()
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/test-connection");
            try
            {
                var ok = _service.TestConnection();
                _logger.LogInformation("✅ TestConnection => {Res}", ok ? "OK" : "FAIL");
                return ok ? Ok("OK") : StatusCode(500, "Error de conexión");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en test-connection: {Msg}", ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno." });
            }
        }

        /* ===========================================================
         *  Insertar (notifica a RRHH para revisión, no a aprobadores)
         * =========================================================== */
        [HttpPost("insertar")]
        public async Task<IActionResult> Insertar([FromBody] SolicitudPersonalDto dto)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/insertar");
            if (dto == null)
                return BadRequest(new { success = false, message = "Payload inválido" });

            try
            {
                var id = _service.Insertar(dto);
                if (id <= 0)
                {
                    _logger.LogWarning("⚠️ Insertar: no se insertó la solicitud");
                    return StatusCode(500, new { success = false, message = "No se insertó la solicitud" });
                }

                _logger.LogInformation("✅ Insert OK id={Id}", id);

                // Notificar a Gestión Humana asignada (si hay)
                try
                {
                    var sol = _service.ObtenerSolicitudPorId(id);
                    if (sol != null)
                    {
                        var correosRrhh = GetCorreosGestionHumana(dto, sol).ToList();
                        if (correosRrhh.Count == 0)
                        {
                            _logger.LogWarning("⚠️ No hay correos de RRHH configurados para revisión. id={Id}", id);
                        }
                        else
                        {
                            foreach (var rrhh in correosRrhh)
                            {
                                await EnviarCorreoRevisionRrhhAsync(sol, rrhh);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ No se pudo cargar la solicitud recién creada para correo. id={Id}", id);
                    }
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "❌ Error enviando correo de revisión RRHH id={Id}: {Msg}", id, exMail.Message);
                }

                return Ok(new { success = true, id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en insertar: {Msg}", ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al insertar" });
            }
        }

        /* ===========================================================
         *  RRHH marca revisado → dispara correo SOLO al aprobador del nivel actual
         *  (NO modifica ap1_/ap2_/ap3_)
         * =========================================================== */
        [HttpPost("revisado-rrhh")]
        public async Task<IActionResult> RevisadoRrhh([FromQuery] int id)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/revisado-rrhh id={Id}", id);
            if (id <= 0)
                return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                {
                    _logger.LogWarning("⚠️ RevisadoRRHH: solicitud no encontrada id={Id}", id);
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });
                }

                // Persistimos sello de envío (opcional, para trazabilidad)
                _service.MarcarRevisadoRrhh(id);

                var (nivel, correos) = _service.ObtenerCorreosAprobadorActual(id);
                if (string.Equals(nivel, "FINAL", StringComparison.OrdinalIgnoreCase) || correos.Count == 0)
                {
                    _logger.LogWarning("⚠️ RevisadoRRHH: no hay aprobador pendiente (nivel={Nivel}) id={Id}", nivel, id);
                    return Ok(new { success = true, message = "Marcado revisado. No hay aprobadores pendientes." });
                }

                foreach (var correoAp in correos.Where(c => !string.IsNullOrWhiteSpace(c)))
                    await EnviarCorreoAprobadorAsync(sol, correoAp.Trim(), nivel);

                return Ok(new { success = true, message = "Revisión RRHH registrada y envío al aprobador del nivel actual." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en revisado-rrhh id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al marcar revisado RRHH" });
            }
        }

        /* ===========================================================
         *  Listar / Catálogos
         * =========================================================== */
        [HttpGet("listar")]
        public IActionResult Listar([FromQuery] string usuarioId)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/listar usuarioId={UsuarioId}", usuarioId);
            try
            {
                var data = _service.Listar(usuarioId);
                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("⚠️ Listar: sin registros para usuarioId={UsuarioId}", usuarioId);
                    return NotFound(new { success = false, message = "Sin registros" });
                }

                _logger.LogInformation("✅ Listar OK total={Total}", data.Count);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en listar usuarioId={UsuarioId}: {Msg}", usuarioId, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al listar" });
            }
        }

        [HttpGet("canales")]
        public IActionResult ListarCanales()
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/canales");
            try
            {
                var data = _service.ListarCanales();
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en canales: {Msg}", ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpGet("cargos-canales")]
        public IActionResult ListarCargosCanales([FromQuery] string? canal = null)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/cargos-canales canal={Canal}", canal);
            try
            {
                var data = _service.ListarCargosCanales(canal);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en cargos-canales canal={Canal}: {Msg}", canal, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        /* ===========================================================
         *  Seleccionado (GH)
         * =========================================================== */
        [HttpPost("seleccionado")]
        public IActionResult GuardarSeleccionado([FromBody] SeleccionadoDto dto)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/seleccionado id={Id}", dto?.Id);

            if (dto is null)
                return BadRequest(new { success = false, message = "Payload inválido" });
            if (dto.Id <= 0)
                return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var ok = _service.GuardarSeleccionado(dto);
                if (!ok)
                {
                    _logger.LogWarning("⚠️ GuardarSeleccionado: sin cambios o fallo BD. id={Id}", dto.Id);
                    return StatusCode(500, new { success = false, message = "No se guardó el seleccionado" });
                }

                _logger.LogInformation("✅ GuardarSeleccionado OK id={Id}", dto.Id);
                return Ok(new { success = true, message = "Datos guardados" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en GuardarSeleccionado id={Id}: {Msg}", dto.Id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al guardar" });
            }
        }

        /* ===========================================================
         *  Acciones (Aprobada / Rechazada) – Flujo secuencial
         * =========================================================== */
        [HttpGet("accion")]
        public async Task<IActionResult> Accion(
            [FromQuery] int id,
            [FromQuery] string estado,
            [FromQuery] string? motivo = null,
            [FromQuery] string? actorEmail = null)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/accion id={Id} estado={Estado} actor={Actor}", id, estado, actorEmail);

            if (id <= 0 || string.IsNullOrWhiteSpace(estado))
                return BadRequest(new { success = false, message = "Parámetros inválidos" });

            var up = estado.Trim().ToUpperInvariant();
            if (up != "APROBADA" && up != "RECHAZADA")
                return BadRequest(new { success = false, message = "Estado inválido. Use APROBADA o RECHAZADA." });

            try
            {
                var ok = _service.AplicarAccion(id, up, motivo, actorEmail);
                if (!ok)
                {
                    _logger.LogWarning("⚠️ Accion: no se actualizó el registro id={Id}", id);
                    return NotFound(new { success = false, message = "No se actualizó el registro" });
                }

                _logger.LogInformation("✅ Accion aplicada id={Id} -> {Estado}", id, up);

                // Cargar solicitud para notificaciones
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                {
                    _logger.LogWarning("⚠️ No se pudo cargar la solicitud para correos id={Id}", id);
                    return Ok(new { success = true, message = "Acción aplicada" });
                }

                // Si RECHAZADA: avisar al solicitante y CC PowerApps; no continuar flujo
                if (up == "RECHAZADA")
                {
                    var destinatarios = DestinatariosSolicitante(sol).ToList();
                    foreach (var d in destinatarios)
                        await EnviarCorreoFinalAsync(sol, d, aprobado: false, motivoRechazo: motivo);

                    return Ok(new { success = true, message = "Acción aplicada (RECHAZADA)" });
                }

                // Si APROBADA: ver si hay siguiente nivel pendiente o ya es FINAL
                var (nivel, correos) = _service.ObtenerCorreosAprobadorActual(id);

                if (string.Equals(nivel, "FINAL", StringComparison.OrdinalIgnoreCase))
                {
                    // Flujo terminó en APROBADA → notificar a solicitante (CC PowerApps)
                    var destinatarios = DestinatariosSolicitante(sol).ToList();
                    foreach (var d in destinatarios)
                        await EnviarCorreoFinalAsync(sol, d, aprobado: true);

                    return Ok(new { success = true, message = "Acción aplicada (APROBADA FINAL)" });
                }

                // Aún hay aprobador pendiente → notificar SOLO a ese aprobador (secuencial)
                foreach (var correoAp in correos.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    await EnviarCorreoAprobadorAsync(sol, correoAp.Trim(), nivel);
                }

                return Ok(new { success = true, message = "Acción aplicada (APROBADA → siguiente aprobador)" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en accion id={Id} estado={Estado}: {Msg}", id, estado, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al aplicar acción" });
            }
        }

        /* ===========================================================
         *  Helpers: Destinatarios, Correo, Templates, URLs
         * =========================================================== */

        private IEnumerable<string> GetCorreosGestionHumana(SolicitudPersonalDto dto, SolicitudPersonal sol)
        {
            // 1) Preferir configuración en appsettings.json → "RRHH:CorreosRevision"
            var arr = _config.GetSection("RRHH:CorreosRevision").Get<string[]>() ?? Array.Empty<string>();
            var cfg = arr.Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();
            if (cfg.Count > 0) return cfg;

            _logger.LogWarning("RRHH:CorreosRevision no configurado. Usando fallback vacío.");
            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> DestinatariosSolicitante(SolicitudPersonal sol)
        {
            // Si tienes correo real del solicitante, úsalo aquí.
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(sol.CorreoJefe))
                list.Add(sol.CorreoJefe.Trim());
            return list.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private async Task EnviarCorreoRevisionRrhhAsync(SolicitudPersonal sol, string destinatario)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = new MailMessage
                {
                    From = new MailAddress(_smtp.User, "Requisición de Personal"),
                    Subject = $"Revisión RRHH — Requisición #{sol.Id}",
                    Body = TemplateCorreoRrhh(sol),
                    IsBodyHtml = true
                };

                mail.To.Add(destinatario);
                mail.CC.Add(CC_POWERAPPS);

                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo RRHH enviado a {To} (id={Id})", destinatario, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enviando correo RRHH a {To} (id={Id}): {Msg}", destinatario, sol.Id, ex.Message);
            }
        }

        private async Task EnviarCorreoAprobadorAsync(SolicitudPersonal sol, string aprobadorEmail, string nivel)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = new MailMessage
                {
                    From = new MailAddress(_smtp.User, "Requisición de Personal"),
                    Subject = $"Aprobación nivel {nivel} — Requisición #{sol.Id}",
                    Body = TemplateCorreoAprobador(sol, aprobadorEmail, nivel),
                    IsBodyHtml = true
                };

                mail.To.Add(aprobadorEmail);
                mail.CC.Add(CC_POWERAPPS);

                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo Aprobador (nivel {Nivel}) enviado a {To} (id={Id})", nivel, aprobadorEmail, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enviando correo Aprobador a {To} (id={Id}): {Msg}", aprobadorEmail, sol.Id, ex.Message);
            }
        }

        private async Task EnviarCorreoFinalAsync(SolicitudPersonal sol, string destinatario, bool aprobado, string? motivoRechazo = null)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = new MailMessage
                {
                    From = new MailAddress(_smtp.User, "Requisición de Personal"),
                    Subject = aprobado
                        ? $"Requisición #{sol.Id} — APROBADA"
                        : $"Requisición #{sol.Id} — RECHAZADA",
                    Body = TemplateCorreoFinal(sol, aprobado, motivoRechazo),
                    IsBodyHtml = true
                };

                mail.To.Add(destinatario);
                mail.CC.Add(CC_POWERAPPS);

                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo Final ({Estado}) enviado a {To} (id={Id})",
                    aprobado ? "APROBADA" : "RECHAZADA", destinatario, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enviando correo Final a {To} (id={Id}): {Msg}", destinatario, sol.Id, ex.Message);
            }
        }

        private SmtpClient BuildSmtp()
        {
            return new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_smtp.User, _smtp.Pass)
            };
        }

        private string TemplateHeader(SolicitudPersonal s)
        {
            string fmt(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();
            var sb = new StringBuilder();
            sb.AppendLine("<table style='width:100%; border-collapse:collapse; margin:8px 0;'>");
            void row(string k, string? v) =>
                sb.AppendLine($"<tr><td style='padding:6px;border:1px solid #ddd;width:34%;'><b>{WebUtility.HtmlEncode(k)}</b></td><td style='padding:6px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(fmt(v))}</td></tr>");

            row("ID", $"#{s.Id}");
            row("Tipo", s.Tipo);
            row("Cargo requerido", s.CargoRequerido);
            row("Jefe inmediato", s.JefeInmediato);
            row("Ciudad", s.CiudadTrabajo);
            row("Salario básico", s.SalarioBasico);
            row("Fecha solicitud", s.FechaSolicitud);
            row("Estado actual", s.Estado);
            row("Nivel aprobación", s.NivelAprobacion);

            sb.AppendLine("</table>");
            return sb.ToString();
        }

        private string TemplateCorreoRrhh(SolicitudPersonal s)
        {
            var header = TemplateHeader(s);
            return $@"
<html>
  <body style='font-family: Arial, sans-serif; background:#f7f7f7; padding:16px;'>
    <div style='max-width:700px;margin:auto;background:#fff;padding:16px;border-radius:8px;border:1px solid #eee;'>
      <h2 style='margin:0 0 8px 0;color:#333;'>Nueva Requisición #{s.Id} — Revisión RRHH</h2>
      <p style='margin:0 0 12px 0;color:#555;'>Por favor, ingresa al sistema para <b>revisar</b> la información y enviar a aprobación.</p>
      {header}
      <p style='margin-top:14px;color:#777;font-size:13px;'>Este correo incluye copia a PowerApps para trazabilidad.</p>
    </div>
  </body>
</html>";
        }

        private string TemplateCorreoAprobador(SolicitudPersonal s, string aprobadorEmail, string nivel)
        {
            var header = TemplateHeader(s);
            var aprobarUrl = BuildAccionUrl(s.Id, "APROBADA", aprobadorEmail);

            return $@"
<html>
  <body style='font-family: Arial, sans-serif; background:#f7f7f7; padding:16px;'>
    <div style='max-width:700px;margin:auto;background:#fff;padding:16px;border-radius:8px;border:1px solid #eee;'>
      <h2 style='margin:0 0 8px 0;color:#333;'>Aprobación nivel {WebUtility.HtmlEncode(nivel)} — Requisición #{s.Id}</h2>
      <p style='margin:0 0 12px 0;color:#555;'>Se requiere tu aprobación para continuar con el proceso.</p>
      {header}
      <div style='margin:18px 0;'>
        <a href='{WebUtility.HtmlEncode(aprobarUrl)}'
           style='display:inline-block; padding:12px 28px; background:#2a858d; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
           APROBAR
        </a>
      </div>
      <p style='margin:10px 0 0 0;color:#777;font-size:12px;'>Si necesitas rechazar, hazlo desde el sistema.</p>
      <p style='margin-top:8px;color:#777;font-size:13px;'>Este correo incluye copia a PowerApps para trazabilidad.</p>
    </div>
  </body>
</html>";
        }

        private string TemplateCorreoFinal(SolicitudPersonal s, bool aprobado, string? motivoRechazo)
        {
            var header = TemplateHeader(s);
            var estadoTxt = aprobado ? "APROBADA" : "RECHAZADA";
            var extra = aprobado
                ? "<p style='margin:10px 0;color:#2f855a;'>La requisición ha sido aprobada en su totalidad.</p>"
                : $"<p style='margin:10px 0;color:#c53030;'>La requisición fue rechazada.{(string.IsNullOrWhiteSpace(motivoRechazo) ? "" : $" Motivo: <b>{WebUtility.HtmlEncode(motivoRechazo)}</b>")}</p>";

            return $@"
<html>
  <body style='font-family: Arial, sans-serif; background:#f7f7f7; padding:16px;'>
    <div style='max-width:700px;margin:auto;background:#fff;padding:16px;border-radius:8px;border:1px solid #eee;'>
      <h2 style='margin:0 0 8px 0;color:#333;'>Requisición #{s.Id} — {estadoTxt}</h2>
      {extra}
      {header}
      <p style='margin-top:14px;color:#777;font-size:13px;'>Este correo incluye copia a PowerApps para trazabilidad.</p>
    </div>
  </body>
</html>";
        }

        private string BuildAccionUrl(int id, string estado, string? actorEmail = null)
        {
            var req = HttpContext.Request;
            var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
            var url = $"{baseUrl}/api/requisiciones/accion?id={id}&estado={Uri.EscapeDataString(estado)}";
            if (!string.IsNullOrWhiteSpace(actorEmail))
                url += $"&actorEmail={Uri.EscapeDataString(actorEmail)}";
            return url;
        }
    }
}
