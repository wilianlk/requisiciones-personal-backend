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
         *  Correos: (1) solicitante (dedupe con aprobadores) (2) aprobador actual (con botones)
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

                // 4) Aprobador(es) del nivel actual
                var (_, correosRaw) = _service.ObtenerCorreosAprobadorActual(id);

                if (EsAdministrativo(sol))
                {
                    // ADMINISTRATIVO: un solo correo al Aprobador 1 (con botones), sin informativo al solicitante
                    var aprobador1 = FirstEmail(correosRaw);
                    if (!string.IsNullOrWhiteSpace(aprobador1))
                        await EnviarCorreoAprobadorAsync(sol, aprobador1);
                    else
                        _logger.LogWarning("⚠️ RevisadoRRHH(ADMIN): sin aprobador1 id={Id}", id);

                    return Ok(new { success = true, message = "Revisión GH registrada. Notificado solo Aprobador 1 (ADMINISTRATIVO)." });
                }

                // COMERCIAL: informativo al solicitante SOLO si no coincide con aprobador; siempre enviar a aprobador(es)
                await NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);

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
        public IActionResult ListarCargosCanales([FromQuery] string? area = null)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/cargos-canales area={Area}", area);
            try
            {
                var data = _service.ListarCargosCanales(area);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en cargos-canales area={Area}: {Msg}", area, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpGet("cargos-administrativos")]
        public IActionResult ListarCargosAdministrativos([FromQuery] string? area = null)
        {
            _logger.LogInformation("➡️  GET /api/requisiciones/cargos-administrativos area={Area}", area);
            try
            {
                var data = _service.ListarCargosAdministrativos(area);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en cargos-administrativos area={Area}: {Msg}", area, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

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
                        // 1) Aviso al solicitante → ya está en VP GH
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN VP GH");

                        // 2) Correo a la Vicepresidencia (con botones)
                        var aVp = GetCorreosVpGh().ToList();
                        if (aVp.Any())
                        {
                            await EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ GuardarSeleccionado: no hay correos configurados en VPGH");
                        }
                    }
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "❌ Error enviando correos de GuardarSeleccionado id={Id}: {Msg}", dto.Id, exMail.Message);
                }

                return Ok(new { success = true, message = "Seleccionado guardado y notificaciones enviadas a VP GH." });
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
         *   - EN APROBACION  → correo a siguiente aprobador + aviso (dedupe con aprobadores)
         *   - EN SELECCION   → correo a GH con botones + aviso a solicitante
         *   - EN VP GH       → correo con botones a VP GH (uno o varios)
         *   - CERRADO        → correos separados a Nómina (uno o varios) y a GH
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
            if (up != "APROBADA" && up != "RECHAZADA" && up != "CERRADO")
                return BadRequest(new { success = false, message = "Estado inválido. Use APROBADA, RECHAZADA o CERRADO." });

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
                        {
                            var (_, correosRaw) = _service.ObtenerCorreosAprobadorActual(id);

                            if (EsAdministrativo(sol))
                            {
                                // ADMINISTRATIVO: solo aprobador del nivel actual (uno), sin informativo al solicitante
                                var aprobador1 = FirstEmail(correosRaw);
                                if (!string.IsNullOrWhiteSpace(aprobador1))
                                    await EnviarCorreoAprobadorAsync(sol, aprobador1);

                                return Ok(new { success = true, message = "APROBADA → siguiente aprobador (ADMINISTRATIVO: solo Aprobador 1)." });
                            }

                            // COMERCIAL: informativo al solicitante SOLO si no coincide con aprobador; y correo(s) al aprobador(es)
                            await NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);

                            foreach (var correoAp in DistinctNormalizedEmails(correosRaw).Where(c => !string.IsNullOrWhiteSpace(c)))
                                await EnviarCorreoAprobadorAsync(sol, correoAp.Trim());

                            return Ok(new { success = true, message = "APROBADA → siguiente aprobador" });
                        }

                    case "EN SELECCION":
                        // Aprobaciones completadas → GH con botones + solicitante
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCIÓN");
                        {
                            var aGh = GetCorreosGestionHumana();
                            if (aGh.Any())
                                await EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCIÓN");
                        }
                        return Ok(new { success = true, message = "APROBADA FINAL → EN SELECCIÓN" });

                    case "EN VP GH":
                        // Ya está en VP GH → notificar VP GH con botones (uno o varios)
                        {
                            var aVp = GetCorreosVpGh().ToList();
                            if (aVp.Any())
                                await EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                            else
                                _logger.LogWarning("⚠️ EN VP GH: no hay correos configurados en VPGH:Correo/Correos");
                        }
                        return Ok(new { success = true, message = "EN VP GH → VP notificado" });

                    case "CERRADO":
                        // VP GH aprobó → Correos separados a Nómina (uno o varios) y GH
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

                var aVp = GetCorreosVpGh().ToList();
                if (aVp.Any())
                    await EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                else
                    _logger.LogWarning("⚠️ vpgh/enviar: no hay correos configurados en VPGH:Correo/Correos");

                return Ok(new { success = true, message = "Estado actualizado a EN VP GH y correo enviado al VP GH (con botones)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en vpgh/enviar id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

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
        private IEnumerable<string> GetCorreosVpGh()
        {
            var one = _config["VPGH:Correo"];
            var many = _config.GetSection("VPGH:Correos").Get<string[]>();
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(one)) list.Add(one.Trim());
            if (many is { Length: > 0 }) list.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var res = DistinctNormalizedEmails(list);
            _logger.LogInformation("VPGH destinatarios: {To}", string.Join(", ", res));
            return res;
        }
        private IEnumerable<string> GetCorreosNomina()
        {
            var one = _config["Nomina:Correo"];
            var many = _config.GetSection("Nomina:Correos").Get<string[]>();
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(one)) list.Add(one.Trim());
            if (many is { Length: > 0 }) list.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var res = DistinctNormalizedEmails(list);
            _logger.LogInformation("Nómina destinatarios: {To}", string.Join(", ", res));
            return res;
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
        private static bool EsAdministrativo(SolicitudPersonal s)
            => string.Equals((s.Tipo ?? "").Trim(), "ADMINISTRATIVO", StringComparison.OrdinalIgnoreCase);
        private static string? FirstEmail(IEnumerable<string> correosRaw)
            => correosRaw?
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?
                .Trim()
                .ToLowerInvariant();
        private async Task NotificarSolicitanteSinDuplicarAprobadoresAsync(
            SolicitudPersonal sol,
            string estadoTitulo,
            IEnumerable<string> aprobadoresRaw
        )
        {
            try
            {
                var aprobadores = DistinctNormalizedEmails(aprobadoresRaw ?? Array.Empty<string>());
                var solicitantes = DistinctNormalizedEmails(DestinatariosSolicitante(sol));
                var soloSolicitantes = solicitantes.Except(aprobadores).ToList();

                if (soloSolicitantes.Count == 0)
                {
                    _logger.LogInformation("ℹ️ Aviso solicitante omitido por coincidencia con aprobador (id={Id})", sol.Id);
                    return;
                }

                foreach (var to in soloSolicitantes)
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
                _logger.LogError(ex, "❌ Error en NotificarSolicitanteSinDuplicarAprobadoresAsync id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }
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
        private async Task EnviarCorreoVpGhConBotonesAsync(SolicitudPersonal sol, IEnumerable<string> destinatariosVpGh)
        {
            try
            {
                var toList = DistinctNormalizedEmails(destinatariosVpGh);
                if (toList.Count == 0)
                {
                    _logger.LogWarning("VP GH: no hay destinatarios para enviar (id={Id}).", sol.Id);
                    return;
                }

                using var smtp = BuildSmtp();
                using var mail = BaseMail(sol,
                    $"Requisición #{sol.Id} — EN VP GH",
                    TemplateCorreoVpGhConBotones(sol));

                foreach (var to in toList) mail.To.Add(to);
                AddCcPowerApps(mail);

                await smtp.SendMailAsync(mail);
                _logger.LogInformation("📧 Correo VP GH (botones) a {To} (id={Id})", string.Join(", ", toList), sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo VP GH (id={Id}): {Msg}", sol.Id, ex.Message);
            }
        }
        private async Task EnviarCorreoCierreANominaYGhAsync(SolicitudPersonal sol)
        {
            try
            {
                var nomina = GetCorreosNomina().ToList();
                var gh = GetCorreosGestionHumana().ToList();

                // 1) Nómina (en un solo correo con todos en "To")
                if (nomina.Any())
                {
                    using var smtp1 = BuildSmtp();
                    using var mail1 = BaseMail(sol, $"Requisición #{sol.Id} — CERRADO", TemplateCorreoCierre(sol));
                    foreach (var to in nomina) mail1.To.Add(to);
                    AddCcPowerApps(mail1);
                    await smtp1.SendMailAsync(mail1);
                    _logger.LogInformation("📧 Cierre enviado a Nómina ({To}) id={Id}", string.Join(", ", nomina), sol.Id);
                }
                else
                {
                    _logger.LogWarning("⚠️ Cierre: no hay correos configurados en Nomina:Correo/Correos");
                }

                // 2) GH (en un solo correo con todos en "To")
                if (gh.Any())
                {
                    using var smtp2 = BuildSmtp();
                    using var mail2 = BaseMail(sol, $"Requisición #{sol.Id} — CERRADO", TemplateCorreoCierre(sol));
                    foreach (var to in gh) mail2.To.Add(to);
                    AddCcPowerApps(mail2);
                    await smtp2.SendMailAsync(mail2);
                    _logger.LogInformation("📧 Cierre enviado a GH ({To}) id={Id}", string.Join(", ", gh), sol.Id);
                }
                else
                {
                    _logger.LogWarning("⚠️ Cierre: no hay correos configurados en RRHH:CorreosRevision");
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
            //sb.AppendLine(row("Estado actual", EstadoTitulo(s.Estado)));
            //sb.AppendLine(row("Acción requerida por", CalcularAccionRequerida(s.Estado, s.NivelAprobacion)));
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
                sb.AppendLine(row("Centro de Costos", s.CentroCostos));
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
        private string TemplateCorreoGhConBotones(SolicitudPersonal s, string estadoTitulo)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

            // Decidir a dónde debe ir el botón "APROBAR" según el estado mostrado en el correo
            string aprobarUrl;
            string info;
            if (estadoTitulo.Contains("REVISIÓN", StringComparison.OrdinalIgnoreCase))
            {
                aprobarUrl = BuildGetUrl($"/api/requisiciones/revisado-rrhh?id={s.Id}");
                info = "";
            }
            else if (estadoTitulo.Contains("SELECCIÓN", StringComparison.OrdinalIgnoreCase))
            {
                aprobarUrl = BuildGetUrl($"/api/requisiciones/vpgh/enviar?id={s.Id}");
                info = "<p style='margin:8px 0;color:#374151;'>Acción de GH: guardar/validar el seleccionado y, si procede, <b>enviar a VP GH</b>.</p>";
            }
            else
            {
                aprobarUrl = BuildAccionUrl(s.Id, "APROBADA");
                info = "";
            }

            var rechazarUrl = BuildFrontRechazoUrl(s.Id);

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
            ";

            var titulo = $"Requisición #{s.Id} — {estadoTitulo}";
            return ShellCorreo(titulo, header + buttons);
        }
        private string TemplateCorreoAprobador(SolicitudPersonal s, string aprobadorEmail)
        {
            var header = EncabezadoBasico(s) + @"
            <div style='margin:8px 0 4px 0;'>
              <span style='display:inline-block;background:#eef2ff;color:#3730a3;padding:6px 10px;border-radius:999px;font-size:12px;font-weight:bold;'>
                Te corresponde aprobar o rechazar esta solicitud
              </span>
            </div>" + DetallePorTipo(s);

            var aprobarUrl = BuildAccionUrl(s.Id, "APROBADA", aprobadorEmail);
            var rechazarUrl = BuildFrontRechazoUrl(s.Id, aprobadorEmail);

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
        private string TemplateCorreoVpGhConBotones(SolicitudPersonal s)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

            var aprobarUrl = BuildAccionUrl(s.Id, "CERRADO");
            var rechazarUrl = BuildFrontRechazoUrl(s.Id);

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
        private string BuildGetUrl(string relativePathAndQuery)
        {
            var req = HttpContext.Request;
            var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
            return $"{baseUrl}{relativePathAndQuery}";
        }

        [HttpGet("listar-por-aprobador")]
        public async Task<IActionResult> ListarPorAprobador([FromQuery] string correo)
        {
            _logger.LogInformation("➡️ GET /api/requisiciones/listar-por-aprobador correo={Correo}", correo);

            try
            {
                if (string.IsNullOrWhiteSpace(correo))
                    return BadRequest(new { success = false, message = "Debe indicar el parámetro correo (correo del aprobador)." });

                var data = await _service.ListarPendientesPorCorreoAprobador(correo);

                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("⚠️ Sin requisiciones pendientes para correo {Correo}", correo);
                    return NotFound(new { success = false, message = "Sin pendientes para aprobar." });
                }

                _logger.LogInformation("✅ {Total} requisiciones encontradas para correo {Correo}", data.Count, correo);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en listar-por-aprobador correo={Correo}", correo);
                return StatusCode(500, new { success = false, message = "Error interno del servidor" });
            }
        }
        private string BuildFrontRechazoUrl(int id, string? actorEmail = null)
        {
            var baseUrl = _config["Frontend:BaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var req = HttpContext?.Request;
                baseUrl = req != null ? $"{req.Scheme}://{req.Host}" : "";
            }

            var url = $"{baseUrl}/rechazar?id={id}";
            if (!string.IsNullOrWhiteSpace(actorEmail))
                url += $"&actorEmail={Uri.EscapeDataString(actorEmail)}";

            return url;
        }

    }
}
