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
        private readonly PdfService _pdfService;
        private readonly IWebHostEnvironment _env;

        private List<string> _ccPowerApps = new();

        public RequisicionesController(
            SolicitudesPersonalService service,
            ILogger<RequisicionesController> logger,
            IOptions<SmtpSettings> smtpOptions,
            IConfiguration config,
            PdfService pdfService,
            IWebHostEnvironment env)
        {
            _service = service;
            _logger = logger;
            _smtp = smtpOptions.Value;
            _config = config;
            _pdfService = pdfService;
            _env = env;

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

        [HttpPost("revisado-rrhh")]
        public async Task<IActionResult> RevisadoRrhh([FromQuery] int id, [FromQuery] string? identificacion = null)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/revisado-rrhh id={Id} ident={Ident}", id, identificacion);
            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            identificacion = !string.IsNullOrWhiteSpace(identificacion)
                ? identificacion.Trim()
                : _config["RRHH:Identificacion"];

            if (string.IsNullOrWhiteSpace(identificacion))
            {
                _logger.LogWarning("⚠️ Sin Identificacion por query ni en appsettings (Identificacion:Default)");
                return StatusCode(500, new { success = false, message = "No hay identificación configurada." });
            }

            var solPrev = _service.ObtenerSolicitudPorId(id);
            if (solPrev == null)
            {
                _logger.LogWarning("⚠️ Solicitud no encontrada id={Id}", id);
                return NotFound(new { success = false, message = "Solicitud no encontrada" });
            }

            var ok = _service.MarcarRevisadoRrhh(id, identificacion);
            if (!ok)
            {
                _logger.LogWarning("⚠️ Fallo al marcar revisado: id={Id}, ident={Ident}", id, identificacion);
                return BadRequest(new { success = false, message = "No se pudo marcar como revisado por GH." });
            }

            var sol = _service.ObtenerSolicitudPorId(id) ?? solPrev;
            var (_, correosRaw) = _service.ObtenerCorreosAprobadorActual(id);

            if (EsAdministrativo(sol))
            {
                var aprobador1 = FirstEmail(correosRaw);
                if (!string.IsNullOrWhiteSpace(aprobador1))
                {
                    await EnviarCorreoAprobadorAsync(sol, aprobador1);
                    _logger.LogInformation("📧 ADMIN: enviado a aprobador1={Aprobador1} id={Id}", aprobador1, id);
                }
                else
                {
                    _logger.LogWarning("⚠️ ADMIN: sin aprobador1 id={Id}", id);
                }
                _logger.LogInformation("✅ RevisadoRRHH ADMIN completado id={Id}", id);
                return Ok(new { success = true, message = "Revisión GH registrada. Notificado Aprobador 1 (ADMINISTRATIVO)." });
            }

            await NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);
            foreach (var correoAp in DistinctNormalizedEmails(correosRaw))
                await EnviarCorreoAprobadorAsync(sol, correoAp);

            _logger.LogInformation("✅ RevisadoRRHH completado id={Id} ident={Ident}", id, identificacion);
            return Ok(new { success = true, message = "Revisión GH registrada y envío al aprobador actual." });
        }

        [HttpGet("revisado-rrhh")]
        public Task<IActionResult> RevisadoRrhhGet([FromQuery] int id, [FromQuery] string? identificacion = null)
            => RevisadoRrhh(id, identificacion);

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

            // ✅ Nuevos estados válidos según la tabla de flujo completa
            var permitidos = new HashSet<string>
    {
        "EN APROBACION",
        "EN REVISION POR GESTION GH",
        "APROBADA",
        "RECHAZADA",
        "CERRADO",
        "APROBADO POR GESTION GH",
        "RECHAZADO POR GESTION GH",
        "EN SELECCION",
        "APROBADO POR SELECCIÓN",
        "RECHAZADO POR SELECCIÓN",
        "EN VP GH",
        "APROBADO POR VP GH",
        "RECHAZADO POR VP GH"
    };

            if (!permitidos.Contains(up))
                return BadRequest(new
                {
                    success = false,
                    message = "Estado inválido. Use uno de los siguientes: " +
                              "EN APROBACION, EN REVISION POR GESTION GH, APROBADA, RECHAZADA, CERRADO, " +
                              "APROBADO/RECHAZADO POR GESTION GH, EN SELECCION, APROBADO/RECHAZADO POR SELECCIÓN, " +
                              "EN VP GH, APROBADO/RECHAZADO POR VP GH."
                });

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

                var estadoActual = (sol.Estado ?? "").Trim().ToUpperInvariant();

                if (up == "RECHAZADA" || up.EndsWith("RECHAZADO POR VP GH") || up.EndsWith("RECHAZADO POR SELECCIÓN"))
                {
                    await EnviarCorreoFinalSolicitanteAsync(sol, aprobado: false, motivoRechazo: motivo);
                    return Ok(new { success = true, message = $"Acción aplicada ({up})" });
                }

                switch (estadoActual)
                {
                    case "EN APROBACION":
                        var (_, correosRaw) = _service.ObtenerCorreosAprobadorActual(id);

                        if (EsAdministrativo(sol))
                        {
                            var aprobador1 = FirstEmail(correosRaw);
                            if (!string.IsNullOrWhiteSpace(aprobador1))
                                await EnviarCorreoAprobadorAsync(sol, aprobador1);

                            return Ok(new { success = true, message = "APROBADA → siguiente aprobador (ADMINISTRATIVO)" });
                        }

                        await NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);

                        foreach (var correoAp in DistinctNormalizedEmails(correosRaw).Where(c => !string.IsNullOrWhiteSpace(c)))
                            await EnviarCorreoAprobadorAsync(sol, correoAp.Trim());

                        return Ok(new { success = true, message = "APROBADA → siguiente aprobador" });

                    case "EN SELECCION":
                        await EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCIÓN");
                        var aGh = GetCorreosGestionHumana();
                        if (aGh.Any())
                            await EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCIÓN");
                        return Ok(new { success = true, message = "APROBADA FINAL → EN SELECCIÓN" });

                    case "EN VP GH":
                        var aVp = GetCorreosVpGh().ToList();
                        if (aVp.Any())
                            await EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                        else
                            _logger.LogWarning("⚠️ EN VP GH: no hay correos configurados en VPGH:Correo/Correos");
                        return Ok(new { success = true, message = "EN VP GH → VP notificado" });

                    case "CERRADO":
                        await EnviarCorreoCierreANominaYGhAsync(sol);
                        return Ok(new { success = true, message = "CERRADO → Nómina y GH notificados" });

                    default:
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
        public async Task<IActionResult> AprobarVpGhCerrar([FromQuery] int id, [FromQuery] string? identificacion = null)
        {
            _logger.LogInformation("➡️  POST /api/requisiciones/vpgh/aprobar id={Id} usuario={User}", id, identificacion ?? "(sin id)");

            if (id <= 0)
                return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });

                if (!string.Equals(sol.Estado?.Trim(), "EN VP GH", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(409, new { success = false, message = "Solo se puede cerrar si la solicitud está en 'EN VP GH'." });

                var ok = _service.CerrarRequisicion(id);
                if (!ok)
                    return StatusCode(500, new { success = false, message = "No se pudo cerrar la requisición" });

                await EnviarCorreoCierreANominaYGhAsync(sol);

                return Ok(new { success = true, message = "Requisición cerrada y notificaciones enviadas (Nómina y GH por separado)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en vpgh/aprobar id={Id} usuario={User}: {Msg}", id, identificacion ?? "(sin id)", ex.Message);
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

                bool adjuntarPdf = estadoTitulo.Trim().ToUpper() == "EN REVISIÓN POR GESTIÓN GH";

                foreach (var to in destinatarios)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BaseMail(sol,
                        $"Requisición #{sol.Id} — {estadoTitulo}",
                        TemplateCorreoInfoSolicitante(sol, estadoTitulo),
                        adjuntarPdf: adjuntarPdf);

                    mail.To.Add(to);
                    AddCcPowerApps(mail);

                    await smtp.SendMailAsync(mail);

                    _logger.LogInformation(
                        "📧 Solicitante notificado ({Estado}) a {To} (adjuntoPdf={Adj}) (id={Id})",
                        estadoTitulo, to, adjuntarPdf, sol.Id
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error correo solicitante ({Estado}) id={Id}: {Msg}",
                    estadoTitulo, sol.Id, ex.Message);
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
        private MailMessage BaseMail(
        SolicitudPersonal s,
        string subject,
        string htmlBody,
        bool adjuntarPdf = false
         )
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(_smtp.User, "Requisición de Personal"),
                    Subject = subject,
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };

                var req = HttpContext.Request;
                string baseUrl = $"{req.Scheme}://{req.Host}";

                string logoCid = Guid.NewGuid().ToString("N");
                string logoPath;

                if (_env.IsDevelopment())
                {
                    logoPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot/img/logorecamier-01.png"
                    );
                }
                else
                {
                    logoPath = Path.Combine(
                        _env.WebRootPath,
                        "img/logorecamier-01.png"
                    );
                }

                string cuerpo = $@"
                <!DOCTYPE html>
                <html lang='es'>
                <head>
                  <meta charset='UTF-8'>
                  <style>
                    body {{
                      font-family: Arial, sans-serif;
                      background-color: #f8f8f8;
                      padding: 16px;
                    }}
                    .logo-container {{
                      text-align: center;
                      padding-bottom: 8px;
                      background-color: #ffffff;
                    }}
                    .container {{
                      background: #ffffff;
                      border: 1px solid #ddd;
                      border-radius: 8px;
                      max-width: 800px;
                      margin: auto;
                      overflow: hidden;
                    }}
                    .body {{
                      padding: 16px 20px;
                    }}
                  </style>
                </head>
                <body>
                  <div class='logo-container'>
                    <img src='cid:{logoCid}' alt='Logo Recamier'
                         style='max-width:300px;width:30%;height:auto;display:block;margin:auto;' />
                  </div>
                  <div class='container'>
                    <div class='body'>
                      {htmlBody}
                    </div>
                  </div>
                </body>
                </html>";

                var htmlView = AlternateView.CreateAlternateViewFromString(cuerpo, Encoding.UTF8, "text/html");

                if (System.IO.File.Exists(logoPath))
                {
                    var logo = new LinkedResource(logoPath, "image/png")
                    {
                        ContentId = logoCid,
                        TransferEncoding = System.Net.Mime.TransferEncoding.Base64
                    };

                    htmlView.LinkedResources.Add(logo);
                }

                mail.AlternateViews.Add(htmlView);

                if (adjuntarPdf)
                {
                    string estadoTitulo = EstadoTitulo(s.Estado);
                    string titulo = $"Requisición #{s.Id} — {estadoTitulo}";

                    string contenido = EncabezadoBasico(s) + DetallePorTipo(s);
                    string htmlCompleto = ShellCorreo(titulo, contenido);

                    var pdfBytes = _pdfService.HtmlToPdf(htmlCompleto);

                    var stream = new MemoryStream(pdfBytes);
                    mail.Attachments.Add(new Attachment(stream, $"Requisicion_{s.Id}.pdf", "application/pdf"));
                }

                return mail;
            }
        private string ShellCorreo(string titulo, string contenidoHtml)
        {
            return $@"
            <html>
              <body style='margin:0;padding:16px;background:#f3f4f6;'>
                <div style=""max-width:840px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;
                            border-radius:8px;overflow:hidden;font-family:Arial, Helvetica, sans-serif;color:#111827;"">
                  <div style='background:#0b4f79;color:#ffffff;padding:16px 20px;'>
                    <h2 style='margin:0;font-size:18px;line-height:1.2;'>{WebUtility.HtmlEncode(titulo)}</h2>
                  </div>
                  <div style='padding:16px 20px;'>
                    {contenidoHtml}
                  </div>
                </div>
              </body>
            </html>";
        }
        private string EncabezadoBasico(SolicitudPersonal s)
        {
            static string H(string? v) =>
                WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim());

            string K(string v) =>
                "<td style='padding:6px 10px;border:1px solid #d1d5db;width:35%;font-weight:bold;background:#f9fafb;" +
                "font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827;'>" +
                WebUtility.HtmlEncode(v) + "</td>";

            string V(string? v) =>
                "<td style='padding:6px 10px;border:1px solid #d1d5db;font-family:Arial,Helvetica,sans-serif;" +
                "font-size:14px;color:#111827;'>" + H(v) + "</td>";

            string Row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var sb = new StringBuilder();
            sb.Append("<table style='width:100%;border-collapse:collapse;table-layout:fixed;border:1px solid #d1d5db;'>");

            sb.Append(Row("ID", $"#{s.Id}"));
            sb.Append(Row("Nivel aprobacion", s.NivelAprobacion));
            sb.Append(Row("Tipo", s.Tipo));
            sb.Append(Row("Cargo requerido", s.CargoRequerido));
            sb.Append(Row("Jefe inmediato", s.JefeInmediato));
            sb.Append(Row("Ciudad", s.CiudadTrabajo));
            sb.Append(Row("Salario basico", s.SalarioBasico));
            sb.Append(Row("Fecha solicitud", s.FechaSolicitud));

            sb.Append("</table>");
            return sb.ToString();
        }
        private string DetallePorTipo(SolicitudPersonal s)
        {
            static string H(string? v) =>
                WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim());

            string K(string v) =>
                "<td style='padding:6px 10px;border:1px solid #d1d5db;width:35%;font-weight:bold;background:#f9fafb;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827;'>" +
                WebUtility.HtmlEncode(v) + "</td>";

            string V(string? v) =>
                "<td style='padding:6px 10px;border:1px solid #d1d5db;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827;'>" +
                H(v) + "</td>";

            string Row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var tipo = (s.Tipo ?? "").Trim().ToUpperInvariant();
            var sb = new StringBuilder();

            sb.Append("<h3 style='margin:14px 0 8px 0;font-family:Arial,Helvetica,sans-serif;font-size:16px;color:#111827;'>Detalle de la solicitud</h3>");
            sb.Append("<table style='width:100%;border-collapse:collapse;table-layout:fixed;border:1px solid #d1d5db;'>");

            if (tipo == "COMERCIAL")
            {
                sb.Append(Row("Vicepresidencia", s.Vicepresidencia));
                sb.Append(Row("Canal", s.Canal));
                sb.Append(Row("Area", s.Area));
                sb.Append(Row("Gerente de Canal", s.GerenteCanal));
                sb.Append(Row("Gerente de Division", s.GerenteDivision));
                sb.Append(Row("Centro de Costos", s.CentroCostos));
                sb.Append(Row("# Terr. asignado", s.TerrAsignado));
                sb.Append(Row("% Cobro automatico", s.CobroAutomatico));
                sb.Append(Row("Horario de trabajo", s.HorarioTrabajo));
                sb.Append(Row("Dias laborales", s.DiasLaborales));
                sb.Append(Row("Zona / ciudades", s.ZonaCiudades));
                sb.Append(Row("Clientes a cargo", s.ClientesCargo));
                sb.Append(Row("Canales a cargo", s.CanalesCargo));
                sb.Append(Row("Auxilio de movilizacion (COP)", s.AuxilioMovilizacion));
                sb.Append(Row("Salario garantizado", s.SalarioGarantizado));
                sb.Append(Row("Meses garantizado", s.MesesGarantizado));
                sb.Append(Row("Promedio variable", s.PromedioVariable));
                sb.Append(Row("Requiere vehiculo/moto", s.RequiereMoto));
                sb.Append(Row("Activar proceso por", s.ActivarProcesoPor));
                sb.Append(Row("Tipo de solicitud", s.TipoSolicitud));
                sb.Append(Row("Persona a reemplazar", s.PersonaReemplaza));
            }
            else
            {
                sb.Append(Row("Vicepresidencia", s.Vicepresidencia));
                sb.Append(Row("Area solicitante", s.AreaSolicitante));
                sb.Append(Row("Cargo jefe inmediato", s.CargoJefeInmediato));
                sb.Append(Row("Tipo de jornada", s.TipoJornada));
                sb.Append(Row("Horario", s.HorarioTrabajo));
                sb.Append(Row("Hora inicio", s.HoraInicio));
                sb.Append(Row("Hora fin", s.HoraFin));
                sb.Append(Row("Dias laborales", s.DiasLaborales));
                sb.Append(Row("Centro de Costos", s.CentroCostos));
                sb.Append(Row("Tipo de solicitud", s.TipoSolicitud));
                sb.Append(Row("Tipo de contrato", s.TipoContrato));
                sb.Append(Row("# Meses (si fijo)", s.MesesContrato?.ToString()));
                sb.Append(Row("Ciudad de trabajo", s.CiudadTrabajo));
                sb.Append(Row("Activar proceso por", s.ActivarProcesoPor));
                sb.Append(Row("Persona a reemplazar", s.PersonaReemplaza));
            }

            sb.Append(Row("Justificacion", s.Justificacion));
            sb.Append("</table>");
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

            // 🔍 Determinar el nivel del aprobador según el correo (ajustado a tu modelo real)
            var nivel = 0;
            if (!string.IsNullOrWhiteSpace(aprobadorEmail))
            {
                if (string.Equals(s.Ap1Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 1;
                else if (string.Equals(s.Ap2Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 2;
                else if (string.Equals(s.Ap3Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 3;
            }

            var rechazarUrl = BuildFrontRechazoUrl(s.Id, aprobadorEmail, nivel);

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

            // ✅ Aprobación directa al backend
            var aprobarUrl = BuildAccionUrl(s.Id, "APROBADO POR VP GH");

            // ✅ Rechazo pasa por el front para capturar motivo
            //    Se agrega el flag ?vpgh=true para que el front sepa de dónde viene
            var rechazarUrl = BuildFrontRechazoUrl(s.Id) + "&vpgh=true";

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

        [HttpGet("listar-pendientes-vpgh")]
        public async Task<IActionResult> ListarPendientesVpGh([FromQuery] string correo)
        {
            _logger.LogInformation("➡️ GET /api/requisiciones/listar-pendientes-vpgh correo={Correo}", correo);

            try
            {
                if (string.IsNullOrWhiteSpace(correo))
                    return BadRequest(new { success = false, message = "Debe indicar el parámetro correo (correo de VP GH)." });

                var data = await _service.ListarPendientesVpGh(correo);

                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("⚠️ Sin requisiciones pendientes para VP GH {Correo}", correo);
                    return NotFound(new { success = false, message = "Sin pendientes para VP GH." });
                }

                _logger.LogInformation("✅ {Total} requisiciones encontradas para VP GH {Correo}", data.Count, correo);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en listar-pendientes-vpgh correo={Correo}", correo);
                return StatusCode(500, new { success = false, message = "Error interno del servidor" });
            }
        }
        private string BuildFrontRechazoUrl(int id, string? actorEmail = null, int? nivel = null)
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
            if (nivel.HasValue && nivel > 0)
                url += $"&nivel={nivel.Value}";

            return url;
        }
        [HttpGet("pdf/{id}")]
        public IActionResult GenerarPdf(int id, [FromServices] PdfService pdfService)
        {
            try
            {
                // 1. Obtener la solicitud
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });

                string estadoTitulo = EstadoTitulo(sol.Estado);
                string titulo = $"Requisición #{sol.Id} — {estadoTitulo}";

                string contenido = EncabezadoBasico(sol) + DetallePorTipo(sol);

                string html = ShellCorreo(titulo, contenido);

                // 5. Convertir HTML → PDF
                var pdfBytes = pdfService.HtmlToPdf(html);

                // 6. Retornar el archivo PDF
                return File(pdfBytes, "application/pdf", $"Requisicion_{sol.Id}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al generar PDF para id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al generar PDF" });
            }
        }


    }
}
