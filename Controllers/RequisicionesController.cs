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

        private List<string> _ccPowerApps = new();

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

            // Cargar CC PowerApps (string o array)
            try
            {
                var ccOne = _config["PowerApps:CC"];
                var ccMany = _config.GetSection("PowerApps:CC").Get<string[]>();

                var cc = new List<string>();
                if (!string.IsNullOrWhiteSpace(ccOne)) cc.Add(ccOne.Trim());
                if (ccMany is { Length: > 0 })
                    cc.AddRange(ccMany.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

                _ccPowerApps = DistinctNormalizedEmails(cc);
                _logger.LogInformation("PowerApps CC cargado: {CC}", string.Join(", ", _ccPowerApps));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo cargar PowerApps:CC");
                _ccPowerApps = new List<string>();
            }
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
         *  Insertar → EN REVISION POR GESTION GH
         *  Correos: (1) solicitante (individual)  (2) GH con botones
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

                // Correos separados: solicitante y GH (GH con botones)
                try
                {
                    var sol = _service.ObtenerSolicitudPorId(id);
                    if (sol != null)
                    {
                        // 1) Solicitante
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN REVISIÓN POR GESTIÓN GH");

                        // 2) GH con botones
                        var aGh = GetCorreosGestionHumana();
                        if (aGh.Any())
                            await EnviarCorreoGhConBotonesAsync(sol, aGh, "EN REVISIÓN POR GESTIÓN GH");
                    }
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "❌ Error envío correos inserción id={Id}: {Msg}", id, exMail.Message);
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
         *  GH marca revisado → EN APROBACION
         *  Correos: (1) solicitante (2) aprobador actual (con botones)
         * =========================================================== */
        [HttpPost("revisado-rrhh")]
        public async Task<IActionResult> RevisadoRrhh([FromQuery] int id)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/revisado-rrhh id={Id}", id);
            if (id <= 0)
                return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                // 1) Validar existencia
                var solPrev = _service.ObtenerSolicitudPorId(id);
                if (solPrev == null)
                {
                    _logger.LogWarning("⚠️ RevisadoRRHH: solicitud no encontrada id={Id}", id);
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });
                }

                // 2) Marcar revisado (pasa a EN APROBACION en BD)
                var ok = _service.MarcarRevisadoRrhh(id);
                if (!ok)
                {
                    _logger.LogWarning("⚠️ RevisadoRRHH: no se pudo marcar revisado id={Id}", id);
                    return StatusCode(500, new { success = false, message = "No se pudo marcar revisado" });
                }

                // 3) Refrescar la solicitud para tener el estado actualizado
                var sol = _service.ObtenerSolicitudPorId(id) ?? solPrev;

                // 4) Aviso al solicitante (correo individual)
                await EnviarCorreoEstadoSolicitanteAsync(sol, "EN APROBACIÓN");

                // 5) Enviar al aprobador actual con botones (sin CC al solicitante)
                var (_, correosRaw) = _service.ObtenerCorreosAprobadorActual(id);
                var correos = DistinctNormalizedEmails(correosRaw);
                if (correos.Count == 0)
                {
                    _logger.LogWarning("⚠️ RevisadoRRHH: no hay aprobador pendiente id={Id}", id);
                    return Ok(new { success = true, message = "Marcado revisado. No hay aprobadores pendientes." });
                }

                foreach (var correoAp in correos)
                    await EnviarCorreoAprobadorAsync(sol, correoAp);

                return Ok(new { success = true, message = "Revisión GH registrada y envío al aprobador del nivel actual." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en revisado-rrhh id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al marcar revisado" });
            }
        }

        // (Para que el botón del correo funcione con GET)
        [HttpGet("revisado-rrhh")]
        public Task<IActionResult> RevisadoRrhhGet([FromQuery] int id) => RevisadoRrhh(id);

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

        [HttpGet("cargos-administrativos")]
        public IActionResult ListarCargosAdministrativos([FromQuery] string? canal = null, [FromQuery] string? area = null)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/cargos-administrativos canal={Canal} area={Area}", canal, area);
            try
            {
                var data = _service.ListarCargosAdministrativos(canal, area);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en cargos-administrativos canal={Canal} area={Area}: {Msg}", canal, area, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        /* ===========================================================
         *  Seleccionado → EN SELECCION
         *  Correos: (1) solicitante  (2) GH con botones
         * =========================================================== */
        [HttpPost("seleccionado")]
        public async Task<IActionResult> GuardarSeleccionado([FromBody] SeleccionadoDto dto)
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

                try
                {
                    var sol = _service.ObtenerSolicitudPorId(dto.Id);
                    if (sol != null)
                    {
                        // 1) Solicitante (correo individual)
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCIÓN");

                        // 2) GH con botones (aprobar selección → EN VP GH)
                        var aGh = GetCorreosGestionHumana();
                        if (aGh.Any())
                            await EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCIÓN");
                    }
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "❌ Error enviando correo de seleccionado id={Id}: {Msg}", dto.Id, exMail.Message);
                }

                return Ok(new { success = true, message = "Datos guardados y notificaciones enviadas" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en GuardarSeleccionado id={Id}: {Msg}", dto.Id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al guardar" });
            }
        }

        /* ===========================================================
         *  Acciones Aprobación / Rechazo (flujo secuencial)
         *  Lógica de envíos post-acción según NUEVO estado:
         *   - EN APROBACION  → correo a siguiente aprobador + aviso independiente a solicitante
         *   - EN SELECCION   → correo a GH con botones + aviso a solicitante
         *   - EN VP GH       → correo con botones a VP GH
         *   - CERRADO        → correos separados a Nómina y a GH
         *   - RECHAZADA      → correo al solicitante
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

                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return Ok(new { success = true, message = "Acción aplicada" });

                // Ramas de notificación según estado ACTUALIZADO
                var estadoActual = (sol.Estado ?? "").Trim().ToUpperInvariant();

                if (up == "RECHAZADA")
                {
                    // Notificar solo al solicitante
                    await EnviarCorreoFinalSolicitanteAsync(sol, aprobado: false, motivoRechazo: motivo);
                    return Ok(new { success = true, message = "Acción aplicada (RECHAZADA)" });
                }

                // APROBADA → depende del estado resultante
                switch (estadoActual)
                {
                    case "EN APROBACION":
                        // 1) Aviso al solicitante
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN APROBACIÓN");
                        // 2) Aprobador actual (sin CC al solicitante)
                        {
                            var (_, correos) = _service.ObtenerCorreosAprobadorActual(id);
                            foreach (var correoAp in correos.Where(c => !string.IsNullOrWhiteSpace(c)))
                                await EnviarCorreoAprobadorAsync(sol, correoAp.Trim());
                        }
                        return Ok(new { success = true, message = "APROBADA → siguiente aprobador" });

                    case "EN SELECCION":
                        // Aprobaciones completadas → GH con botones + solicitante
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCIÓN");
                        {
                            var aGh = GetCorreosGestionHumana();
                            if (aGh.Any())
                                await EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCIÓN");
                        }
                        return Ok(new { success = true, message = "APROBADA FINAL → EN SELECCIÓN (GH notificado con botones)" });

                    case "EN VP GH":
                        // Ya está en VP GH → notificar VP GH con botones
                        {
                            var correoVp = _config.GetValue<string>("VPGH:Correo");
                            if (!string.IsNullOrWhiteSpace(correoVp))
                                await EnviarCorreoVpGhConBotonesAsync(sol, correoVp!.Trim());
                        }
                        return Ok(new { success = true, message = "EN VP GH → VP notificado con botones" });

                    case "CERRADO":
                        // VP GH aprobó → Correos separados a Nómina y GH
                        await EnviarCorreoCierreANominaYGhAsync(sol);
                        return Ok(new { success = true, message = "CERRADO → Nómina y GH notificados" });

                    default:
                        // Fallback: solo avisar al solicitante del estado actual
                        await EnviarCorreoEstadoSolicitanteAsync(sol, EstadoTitulo(sol.Estado));
                        return Ok(new { success = true, message = $"Acción aplicada, estado actual: {estadoActual}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en accion id={Id} estado={Estado}: {Msg}", id, estado, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al aplicar acción" });
            }
        }

        /* ===========================================================
         *  VP GH: pasar a "EN VP GH" y notificar a VP GH (con botones)
         * =========================================================== */
        [HttpPost("vpgh/enviar")]
        public async Task<IActionResult> EnviarAVpGh([FromQuery] int id)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/vpgh/enviar id={Id}", id);
            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                // Solo permitir desde EN SELECCION
                if (!string.Equals(sol.Estado?.Trim(), "EN SELECCION", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(409, new { success = false, message = "La solicitud debe estar en 'EN SELECCION' para enviarse a VP GH." });

                var ok = _service.ActualizarEstadoEnVpGh(id);
                if (!ok) return StatusCode(500, new { success = false, message = "No se actualizó el estado EN VP GH" });

                var vpGhCorreo = _config.GetValue<string>("VPGH:Correo");
                if (!string.IsNullOrWhiteSpace(vpGhCorreo))
                    await EnviarCorreoVpGhConBotonesAsync(sol, vpGhCorreo.Trim());

                return Ok(new { success = true, message = "Estado actualizado a EN VP GH y correo enviado al VP GH (con botones)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en vpgh/enviar id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        /* ===========================================================
         *  VP GH: aprobar → "CERRADO"
         *  Correos: separados a Nómina y GH
         * =========================================================== */
        [HttpPost("vpgh/aprobar")]
        public async Task<IActionResult> AprobarVpGhCerrar([FromQuery] int id)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/vpgh/aprobar id={Id}", id);
            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                // Solo cerrar desde EN VP GH
                if (!string.Equals(sol.Estado?.Trim(), "EN VP GH", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(409, new { success = false, message = "Solo se puede cerrar si la solicitud está en 'EN VP GH'." });

                var ok = _service.CerrarRequisicion(id);
                if (!ok) return StatusCode(500, new { success = false, message = "No se pudo cerrar la requisición" });

                await EnviarCorreoCierreANominaYGhAsync(sol);

                return Ok(new { success = true, message = "Requisición cerrada y notificaciones enviadas (Nómina y GH por separado)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en vpgh/aprobar id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        /* ===========================================================
         *  Helpers: correos, SMTP, templates, URLs
         * =========================================================== */

        private static string NormalizeEmail(string? s)
            => (s ?? "").Trim().ToLowerInvariant();

        private static List<string> DistinctNormalizedEmails(IEnumerable<string> emails)
            => emails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(NormalizeEmail)
                .Distinct()
                .ToList();

        private IEnumerable<string> GetCorreosGestionHumana()
        {
            var arr = _config.GetSection("RRHH:CorreosRevision").Get<string[]>() ?? Array.Empty<string>();
            return DistinctNormalizedEmails(arr);
        }

        private IEnumerable<string> DestinatariosSolicitante(SolicitudPersonal sol)
        {
            // Si más adelante agregas correo del solicitante real, añádelo aquí.
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(sol.CorreoJefe))
                list.Add(sol.CorreoJefe);
            return DistinctNormalizedEmails(list);
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

        private void AddCcPowerApps(MailMessage mail)
        {
            foreach (var cc in _ccPowerApps)
                mail.CC.Add(cc);
        }

        private static string EstadoTitulo(string? estado)
        {
            var e = (estado ?? "").Trim().ToUpperInvariant();
            return e switch
            {
                "EN REVISION POR GESTION GH" => "EN REVISIÓN POR GESTIÓN GH",
                "EN APROBACION" => "EN APROBACIÓN",
                "EN SELECCION" => "EN SELECCIÓN",
                "EN VP GH" => "EN VP GH",
                "APROBADA" => "APROBADA",
                "RECHAZADA" => "RECHAZADA",
                "CERRADO" => "CERRADO",
                _ => string.IsNullOrWhiteSpace(estado) ? "-" : estado.Trim()
            };
        }

        private static string CalcularAccionRequerida(string? estado, string? nivel)
        {
            var e = (estado ?? "").Trim().ToUpperInvariant();
            var n = (nivel ?? "").Trim().ToUpperInvariant();

            return e switch
            {
                "EN REVISION POR GESTION GH" => "Gestión Humana",
                "EN APROBACION" => string.IsNullOrWhiteSpace(n) || n == "FINAL" ? "Aprobador" : $"Aprobador nivel {n}",
                "EN SELECCION" => "Gestión Humana",
                "EN VP GH" => "VP GH",
                "APROBADA" => "-",
                "RECHAZADA" => "-",
                "CERRADO" => "-",
                _ => "-"
            };
        }

        /* ===================== ENVÍOS SMTP ===================== */

        // --- Correos informativos al solicitante (siempre separado) ---
        private async Task EnviarCorreoEstadoSolicitanteAsync(SolicitudPersonal sol, string estadoTitulo)
        {
            try
            {
                var destinatarios = DestinatariosSolicitante(sol);
                foreach (var to in destinatarios)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BaseMail(sol,
                        $"Requisición #{sol.Id} — {estadoTitulo}",
                        TemplateCorreoInfoSolicitante(sol, estadoTitulo));
                    mail.To.Add(to);
                    AddCcPowerApps(mail);
                    await smtp.SendMailAsync(mail);
                    _logger.LogInformation("📧 Solicitante notificado ({Estado}) a {To} (id={Id})", estadoTitulo, to, sol.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo solicitante ({Estado}) id={Id}: {Msg}", estadoTitulo, sol.Id, ex.Message);
            }
        }

        private async Task EnviarCorreoFinalSolicitanteAsync(SolicitudPersonal sol, bool aprobado, string? motivoRechazo = null)
        {
            try
            {
                var destinatarios = DestinatariosSolicitante(sol);
                foreach (var to in destinatarios)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BaseMail(sol,
                        aprobado ? $"Requisición #{sol.Id} — APROBADA" : $"Requisición #{sol.Id} — RECHAZADA",
                        TemplateCorreoFinalSolicitante(sol, aprobado, motivoRechazo));
                    mail.To.Add(to);
                    AddCcPowerApps(mail);
                    await smtp.SendMailAsync(mail);
                    _logger.LogInformation("📧 Correo Final a solicitante ({Estado}) {To} (id={Id})",
                        aprobado ? "APROBADA" : "RECHAZADA", to, sol.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo final solicitante id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }

        // --- GH con botones (Revisión / Selección) ---
        private async Task EnviarCorreoGhConBotonesAsync(SolicitudPersonal sol, IEnumerable<string> destinatariosGh, string estadoTitulo)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = BaseMail(sol,
                    $"Requisición #{sol.Id} — {estadoTitulo}",
                    TemplateCorreoGhConBotones(sol, estadoTitulo));
                foreach (var to in destinatariosGh)
                    mail.To.Add(to);
                AddCcPowerApps(mail);
                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 GH notificado con botones ({Estado}) a: {To} (id={Id})",
                    estadoTitulo, string.Join(", ", mail.To.Select(t => t.Address)), sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo GH con botones ({Estado}) id={Id}: {Msg}", estadoTitulo, sol.Id, ex.Message);
            }
        }

        // --- Aprobador con botones (sin CC al solicitante) ---
        private async Task EnviarCorreoAprobadorAsync(SolicitudPersonal sol, string aprobadorEmail)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = BaseMail(sol, $"Requisición #{sol.Id} — {EstadoTitulo(sol.Estado)}",
                    TemplateCorreoAprobador(sol, aprobadorEmail));
                mail.To.Add(aprobadorEmail);
                // NO CC al solicitante (se envía aparte)
                AddCcPowerApps(mail);
                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo Aprobador enviado a {To} (id={Id})", aprobadorEmail, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enviando correo Aprobador a {To} (id={Id}): {Msg}", aprobadorEmail, sol.Id, ex.Message);
            }
        }

        // --- VP GH con botones ---
        private async Task EnviarCorreoVpGhConBotonesAsync(SolicitudPersonal sol, string correoVpGh)
        {
            try
            {
                using var smtp = BuildSmtp();
                using var mail = BaseMail(sol,
                    $"Requisición #{sol.Id} — EN VP GH",
                    TemplateCorreoVpGhConBotones(sol));
                mail.To.Add(correoVpGh);
                AddCcPowerApps(mail);
                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo VP GH (botones) a {To} (id={Id})", correoVpGh, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo VP GH a {To} (id={Id}): {Msg}", correoVpGh, sol.Id, ex.Message);
            }
        }

        // --- Cierre: correos separados a Nómina y GH ---
        private async Task EnviarCorreoCierreANominaYGhAsync(SolicitudPersonal sol)
        {
            try
            {
                var nomina = (_config.GetValue<string>("Nomina:Correo") ?? "").Trim();
                var gh = GetCorreosGestionHumana().ToList();

                // 1) Nómina
                if (!string.IsNullOrWhiteSpace(nomina))
                {
                    using var smtp1 = BuildSmtp();
                    using var mail1 = BaseMail(sol, $"Requisición #{sol.Id} — CERRADO", TemplateCorreoCierre(sol));
                    mail1.To.Add(nomina);
                    AddCcPowerApps(mail1);
                    await smtp1.SendMailAsync(mail1);
                    _logger.LogInformation("📧 Cierre enviado a Nómina ({To}) id={Id}", nomina, sol.Id);
                }

                // 2) GH
                if (gh.Any())
                {
                    using var smtp2 = BuildSmtp();
                    using var mail2 = BaseMail(sol, $"Requisición #{sol.Id} — CERRADO", TemplateCorreoCierre(sol));
                    foreach (var to in gh) mail2.To.Add(to);
                    AddCcPowerApps(mail2);
                    await smtp2.SendMailAsync(mail2);
                    _logger.LogInformation("📧 Cierre enviado a GH ({To}) id={Id}", string.Join(", ", gh), sol.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correos de Cierre id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }

        private MailMessage BaseMail(SolicitudPersonal s, string subject, string htmlBody)
        {
            return new MailMessage
            {
                From = new MailAddress(_smtp.User, "Requisición de Personal"),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
        }

        /* ===================== Templates & URLs ===================== */

        private string ShellCorreo(string titulo, string contenidoHtml)
        {
            return $@"
<html>
  <body style='font-family: Arial, sans-serif; background:#f7f7f7; padding:16px;'>
    <div style='max-width:800px;margin:auto;background:#fff;padding:16px;border-radius:8px;border:1px solid #eee;'>
      <h2 style='margin:0 0 8px 0;color:#333;'>{WebUtility.HtmlEncode(titulo)}</h2>
      {contenidoHtml}
      <div style='margin-top:18px;color:#888;font-size:12px;'>Este es un mensaje automático. No responder a este correo.</div>
    </div>
  </body>
</html>";
        }

        private string EncabezadoBasico(SolicitudPersonal s)
        {
            string K(string v) => $"<td style='padding:6px;border:1px solid #ddd;width:36%;'><b>{WebUtility.HtmlEncode(v)}</b></td>";
            string V(string? v) => $"<td style='padding:6px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim())}</td>";
            string row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var sb = new StringBuilder();
            sb.AppendLine("<table style='width:100%; border-collapse:collapse; margin:8px 0;'>");
            sb.AppendLine(row("ID", $"#{s.Id}"));
            sb.AppendLine(row("Estado actual", EstadoTitulo(s.Estado)));
            sb.AppendLine(row("Acción requerida por", CalcularAccionRequerida(s.Estado, s.NivelAprobacion)));
            sb.AppendLine(row("Nivel aprobación", s.NivelAprobacion));
            sb.AppendLine(row("Tipo", s.Tipo));
            sb.AppendLine(row("Cargo requerido", s.CargoRequerido));
            sb.AppendLine(row("Jefe inmediato", s.JefeInmediato));
            sb.AppendLine(row("Ciudad", s.CiudadTrabajo));
            sb.AppendLine(row("Salario básico", s.SalarioBasico));
            sb.AppendLine(row("Fecha solicitud", s.FechaSolicitud));
            sb.AppendLine("</table>");
            return sb.ToString();
        }

        // Sección específica por tipo (campos alineados a tu UI)
        private string DetallePorTipo(SolicitudPersonal s)
        {
            string K(string v) => $"<td style='padding:6px;border:1px solid #ddd;width:36%;'><b>{WebUtility.HtmlEncode(v)}</b></td>";
            string V(string? v) => $"<td style='padding:6px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim())}</td>";
            string row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var tipo = (s.Tipo ?? "").Trim().ToUpperInvariant();
            var sb = new StringBuilder();

            sb.AppendLine("<h3 style='margin:14px 0 6px 0;color:#333;'>Detalle de la solicitud</h3>");
            sb.AppendLine("<table style='width:100%; border-collapse:collapse; margin:8px 0;'>");

            if (tipo == "COMERCIAL")
            {
                sb.AppendLine(row("Vicepresidencia", s.Vicepresidencia));
                sb.AppendLine(row("Canal", s.Canal));
                sb.AppendLine(row("Área", s.Area));
                sb.AppendLine(row("Gerente de Canal", s.GerenteCanal));
                sb.AppendLine(row("Gerente de División", s.GerenteDivision));
                sb.AppendLine(row("Centro de Costos", s.CentroCostos));
                sb.AppendLine(row("# Terr. asignado", s.TerrAsignado));
                sb.AppendLine(row("% Cobro automático", s.CobroAutomatico));
                sb.AppendLine(row("Horario de trabajo", s.HorarioTrabajo));
                sb.AppendLine(row("Días laborales", s.DiasLaborales));
                sb.AppendLine(row("Zona / ciudades", s.ZonaCiudades));
                sb.AppendLine(row("Clientes a cargo", s.ClientesCargo));
                sb.AppendLine(row("Canales a cargo (checklist)", s.CanalesCargo));
                sb.AppendLine(row("Auxilio de movilización (COP)", s.AuxilioMovilizacion));
                sb.AppendLine(row("Salario garantizado", s.SalarioGarantizado));
                sb.AppendLine(row("Meses garantizado", s.MesesGarantizado));
                sb.AppendLine(row("Promedio variable", s.PromedioVariable));
                sb.AppendLine(row("Requiere vehículo/moto", s.RequiereMoto));
                sb.AppendLine(row("Activar proceso por", s.ActivarProcesoPor));
                sb.AppendLine(row("Tipo de solicitud", s.TipoSolicitud));
                sb.AppendLine(row("Persona a reemplazar", s.PersonaReemplaza));
            }
            else // ADMINISTRATIVO (u otro)
            {
                sb.AppendLine(row("Vicepresidencia", s.Vicepresidencia));
                sb.AppendLine(row("Área solicitante", s.AreaSolicitante));
                sb.AppendLine(row("Cargo jefe inmediato", s.CargoJefeInmediato));
                sb.AppendLine(row("Tipo de jornada", s.TipoJornada));
                sb.AppendLine(row("Horario (texto)", s.HorarioTrabajo));
                sb.AppendLine(row("Hora inicio", s.HoraInicio));
                sb.AppendLine(row("Hora fin", s.HoraFin));
                sb.AppendLine(row("Días laborales", s.DiasLaborales));
                sb.AppendLine(row("Centro de Costos (opcional F)", s.CentroCostosF));
                sb.AppendLine(row("Tipo de solicitud", s.TipoSolicitud));
                sb.AppendLine(row("Tipo de contrato", s.TipoContrato));
                sb.AppendLine(row("# Meses (si fijo)", s.MesesContrato));
                sb.AppendLine(row("Ciudad de trabajo", s.CiudadTrabajo));
                sb.AppendLine(row("Activar proceso por", s.ActivarProcesoPor));
                sb.AppendLine(row("Persona a reemplazar", s.PersonaReemplaza));
            }

            sb.AppendLine(row("Justificación", s.Justificacion));
            sb.AppendLine("</table>");
            return sb.ToString();
        }

        // ---------- Templates (Solicitante / GH / Aprobador / VP GH / Cierre) ----------

        private string TemplateCorreoInfoSolicitante(SolicitudPersonal s, string estadoTitulo)
        {
            var titulo = $"Requisición #{s.Id} — {estadoTitulo}";
            var body = EncabezadoBasico(s) + DetallePorTipo(s);
            return ShellCorreo(titulo, body);
        }

        private string TemplateCorreoFinalSolicitante(SolicitudPersonal s, bool aprobado, string? motivoRechazo)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);
            var extra = aprobado
                ? ""
                : $"<p style='margin:10px 0;color:#c53030;'>Requisición rechazada{(string.IsNullOrWhiteSpace(motivoRechazo) ? "" : $": <b>{WebUtility.HtmlEncode(motivoRechazo)}</b>")}.</p>";

            var titulo = $"Requisición #{s.Id} — {(aprobado ? "APROBADA" : "RECHAZADA")}";
            return ShellCorreo(titulo, extra + header);
        }

        // GH con botones (para estados: EN REVISIÓN / EN SELECCIÓN)
        private string TemplateCorreoGhConBotones(SolicitudPersonal s, string estadoTitulo)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

            // Decidir a dónde debe ir el botón "APROBAR" según el estado mostrado en el correo
            string aprobarUrl;
            string info;
            if (estadoTitulo.Contains("REVISIÓN", StringComparison.OrdinalIgnoreCase))
            {
                // GH valida y envía a aprobación (EN APROBACIÓN)
                aprobarUrl = BuildGetUrl($"/api/requisiciones/revisado-rrhh?id={s.Id}");
                info = "<p style='margin:8px 0;color:#374151;'>Acción de GH: validar y <b>enviar a aprobación</b>.</p>";
            }
            else if (estadoTitulo.Contains("SELECCIÓN", StringComparison.OrdinalIgnoreCase))
            {
                // GH aprueba la selección y envía a VP GH
                aprobarUrl = BuildGetUrl($"/api/requisiciones/vpgh/enviar?id={s.Id}");
                info = "<p style='margin:8px 0;color:#374151;'>Acción de GH: guardar/validar el seleccionado y, si procede, <b>enviar a VP GH</b>.</p>";
            }
            else
            {
                // Fallback defensivo (no debería ocurrir)
                aprobarUrl = BuildAccionUrl(s.Id, "APROBADA");
                info = "";
            }

            // Rechazo: permanece usando /accion?estado=RECHAZADA
            var rechazarUrl = BuildAccionUrl(s.Id, "RECHAZADA");

            var buttons = $@"
{info}
<div style='margin:18px 0; display:flex; gap:12px;'>
  <a href='{WebUtility.HtmlEncode(aprobarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#16a34a; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     APROBAR
  </a>
  <a href='{WebUtility.HtmlEncode(rechazarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#dc2626; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     RECHAZAR
  </a>
</div>
<p style='font-size:12px;color:#666;margin-top:8px;'>Use los botones para continuar el flujo.</p>";

            var titulo = $"Requisición #{s.Id} — {estadoTitulo}";
            return ShellCorreo(titulo, header + buttons);
        }

        // Aprobador con botones
        private string TemplateCorreoAprobador(SolicitudPersonal s, string aprobadorEmail)
        {
            var header = EncabezadoBasico(s) + @"
<div style='margin:8px 0 4px 0;'>
  <span style='display:inline-block;background:#eef2ff;color:#3730a3;padding:6px 10px;border-radius:999px;font-size:12px;font-weight:bold;'>
    Te corresponde aprobar o rechazar esta solicitud
  </span>
</div>" + DetallePorTipo(s);

            var aprobarUrl = BuildAccionUrl(s.Id, "APROBADA", aprobadorEmail);
            var rechazarUrl = BuildAccionUrl(s.Id, "RECHAZADA", aprobadorEmail);

            var buttons = $@"
<div style='margin:18px 0; display:flex; gap:12px;'>
  <a href='{WebUtility.HtmlEncode(aprobarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#16a34a; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     APROBAR
  </a>
  <a href='{WebUtility.HtmlEncode(rechazarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#dc2626; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     RECHAZAR
  </a>
</div>
<p style='font-size:12px;color:#666;margin-top:8px;'>Si deseas indicar motivo de rechazo, puedes responder a este correo.</p>";

            var titulo = $"Requisición #{s.Id} — EN APROBACIÓN";
            return ShellCorreo(titulo, header + buttons);
        }

        // VP GH con botones
        private string TemplateCorreoVpGhConBotones(SolicitudPersonal s)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

            var aprobarUrl = BuildAccionUrl(s.Id, "APROBADA");   // → CERRADO
            var rechazarUrl = BuildAccionUrl(s.Id, "RECHAZADA");  // → RECHAZADA (avisa a solicitante)

            var buttons = $@"
<div style='margin:18px 0; display:flex; gap:12px;'>
  <a href='{WebUtility.HtmlEncode(aprobarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#16a34a; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     APROBAR (Cerrar)
  </a>
  <a href='{WebUtility.HtmlEncode(rechazarUrl)}'
     style='display:inline-block; padding:12px 20px; background:#dc2626; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
     RECHAZAR
  </a>
</div>";

            var titulo = $"Requisición #{s.Id} — EN VP GH";
            return ShellCorreo(titulo, header + buttons);
        }

        private string TemplateCorreoCierre(SolicitudPersonal s)
        {
            var titulo = $"Requisición #{s.Id} — CERRADO";
            return ShellCorreo(titulo, EncabezadoBasico(s) + DetallePorTipo(s));
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

        // Helper para construir URLs GET absolutas para correos (revisado-rrhh / vpgh/enviar)
        private string BuildGetUrl(string relativePathAndQuery)
        {
            var req = HttpContext.Request;
            var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
            return $"{baseUrl}{relativePathAndQuery}";
        }

        private static string EstadoVisible(string? raw)
        {
            var e = (raw ?? "").Trim().ToUpperInvariant();
            return e switch
            {
                "EN REVISION POR GESTION GH" => "EN REVISIÓN POR GESTIÓN GH",
                "EN APROBACION" => "EN APROBACIÓN",
                "EN SELECCION" => "EN SELECCIÓN",
                "APROBADA" => "APROBADA",
                "RECHAZADA" => "RECHAZADA",
                "EN VP GH" => "EN VP GH",
                "CERRADO" => "CERRADO",
                _ => string.IsNullOrWhiteSpace(e) ? "-" : e
            };
        }
    }
}
