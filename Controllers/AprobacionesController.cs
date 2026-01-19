using System;
using System.Linq;
using System.Threading.Tasks;
using BackendRequisicionPersonal.Helpers;
using BackendRequisicionPersonal.Models.Enums;
using BackendRequisicionPersonal.Services;
using BackendRequisicionPersonal.Services.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AprobacionesController : ControllerBase
    {
        private readonly SolicitudesPersonalService _service;
        private readonly ILogger<AprobacionesController> _logger;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public AprobacionesController(
            SolicitudesPersonalService service,
            ILogger<AprobacionesController> logger,
            IConfiguration config,
            IEmailService emailService)
        {
            _service = service;
            _logger = logger;
            _config = config;
            _emailService = emailService;
        }

        [HttpPost("revisado-rrhh")]
        public async Task<IActionResult> RevisadoRrhh([FromQuery] int id, [FromQuery] string? identificacion = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/revisado-rrhh id={Id} ident={Ident}", id, identificacion);
            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            identificacion = !string.IsNullOrWhiteSpace(identificacion)
                ? identificacion.Trim()
                : _config["RRHH:Identificacion"];

            if (string.IsNullOrWhiteSpace(identificacion))
            {
                _logger.LogWarning("?? Sin Identificacion por query ni en appsettings");
                return StatusCode(500, new { success = false, message = "No hay identificación configurada." });
            }

            var solPrev = _service.ObtenerSolicitudPorId(id);
            if (solPrev == null)
            {
                _logger.LogWarning("?? Solicitud no encontrada id={Id}", id);
                return NotFound(new { success = false, message = "Solicitud no encontrada" });
            }

            var ok = _service.MarcarRevisadoRrhh(id, identificacion);
            if (!ok)
            {
                _logger.LogWarning("?? Fallo al marcar revisado: id={Id}, ident={Ident}", id, identificacion);
                return BadRequest(new { success = false, message = "No se pudo marcar como revisado por GH." });
            }

            var sol = _service.ObtenerSolicitudPorId(id) ?? solPrev;
            var (_, correosRaw) = _service.ObtenerCorreosAprobadorCurrent(id); // Updated method name

            if (EsAdministrativo(sol))
            {
                var aprobador1 = EmailHelper.FirstEmail(correosRaw);
                if (!string.IsNullOrWhiteSpace(aprobador1))
                {
                    await _emailService.EnviarCorreoAprobadorAsync(sol, aprobador1);
                    _logger.LogInformation("?? ADMIN: enviado a aprobador1={Aprobador1} id={Id}", aprobador1, id);
                }
                else
                {
                    _logger.LogWarning("?? ADMIN: sin aprobador1 id={Id}", id);
                }
                _logger.LogInformation("? RevisadoRRHH ADMIN completado id={Id}", id);
                return Ok(new { success = true, message = "Revisión GH registrada. Notificado Aprobador 1 (ADMINISTRATIVO)." });
            }

            await _emailService.NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);
            foreach (var correoAp in EmailHelper.DistinctNormalizedEmails(correosRaw))
                await _emailService.EnviarCorreoAprobadorAsync(sol, correoAp);

            _logger.LogInformation("? RevisadoRRHH completado id={Id} ident={Ident}", id, identificacion);
            return Ok(new { success = true, message = "Revisión GH registrada y envío al aprobador actual." });
        }

        [HttpGet("revisado-rrhh")]
        public Task<IActionResult> RevisadoRrhhGet([FromQuery] int id, [FromQuery] string? identificacion = null)
            => RevisadoRrhh(id, identificacion);

        [HttpGet("accion")]
        public async Task<IActionResult> Accion(
            [FromQuery] int id,
            [FromQuery] string estado,
            [FromQuery] string? motivo = null,
            [FromQuery] string? actorEmail = null)
        {
            _logger.LogInformation("??  GET /api/aprobaciones/accion id={Id} estado={Estado} actor={Actor}", id, estado, actorEmail);

            if (id <= 0 || string.IsNullOrWhiteSpace(estado))
                return BadRequest(new { success = false, message = "Parámetros inválidos" });

            var estadoEnum = estado.ToEstadoRequisicion();
            if (!estadoEnum.HasValue)
            {
                var estadosValidos = EnumExtensions.GetEstadosValidos();
                return BadRequest(new
                {
                    success = false,
                    message = $"Estado inválido '{estado}'. Estados válidos: {string.Join(", ", estadosValidos)}"
                });
            }

            var estadosPermitidos = new[]
            {
                EstadoRequisicion.EnAprobacion,
                EstadoRequisicion.EnRevisionPorGh,
                EstadoRequisicion.Aprobada,
                EstadoRequisicion.Rechazada,
                EstadoRequisicion.Cerrado,
                EstadoRequisicion.EnSeleccion,
                EstadoRequisicion.EnVpGh
            };

            if (!estadosPermitidos.Contains(estadoEnum.Value))
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"El estado '{estado}' no está permitido para esta acción"
                });
            }

            try
            {
                var estadoStr = estadoEnum.Value.GetDescription();
                var ok = _service.AplicarAccion(id, estadoStr, motivo, actorEmail);
                
                if (!ok)
                {
                    _logger.LogWarning("?? Accion: no se actualizó el registro id={Id}", id);
                    return NotFound(new { success = false, message = "No se actualizó el registro" });
                }

                _logger.LogInformation("? Accion aplicada id={Id} -> {Estado}", id, estadoStr);

                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return Ok(new { success = true, message = "Acción aplicada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();

                if (estadoActual?.EsRechazado() == true)
                {
                    await _emailService.EnviarCorreoFinalSolicitanteAsync(sol, aprobado: false, motivoRechazo: motivo);
                    return Ok(new { success = true, message = $"Acción aplicada ({estadoStr})" });
                }

                switch (estadoActual)
                {
                    case EstadoRequisicion.EnAprobacion:
                        var (_, correosRaw) = _service.ObtenerCorreosAprobadorCurrent(id);

                        if (EsAdministrativo(sol))
                        {
                            var aprobador1 = EmailHelper.FirstEmail(correosRaw);
                            if (!string.IsNullOrWhiteSpace(aprobador1))
                                await _emailService.EnviarCorreoAprobadorAsync(sol, aprobador1);

                            return Ok(new { success = true, message = "APROBADA ? siguiente aprobador (ADMINISTRATIVO)" });
                        }

                        await _emailService.NotificarSolicitanteSinDuplicarAprobadoresAsync(sol, "EN APROBACIÓN", correosRaw);

                        foreach (var correoAp in EmailHelper.DistinctNormalizedEmails(correosRaw).Where(c => !string.IsNullOrWhiteSpace(c)))
                            await _emailService.EnviarCorreoAprobadorAsync(sol, correoAp.Trim());

                        return Ok(new { success = true, message = "APROBADA ? siguiente aprobador" });

                    case EstadoRequisicion.EnSeleccion:
                        await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCIÓN");
                        var aGh = GetCorreosGestionHumana();
                        if (aGh.Any())
                            await _emailService.EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCIÓN");
                        return Ok(new { success = true, message = "APROBADA FINAL ? EN SELECCIÓN" });

                    case EstadoRequisicion.EnVpGh:
                        var aVp = GetCorreosVpGh().ToList();
                        if (aVp.Any())
                            await _emailService.EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                        else
                            _logger.LogWarning("?? EN VP GH: no hay correos configurados en VPGH:Correo/Correos");
                        return Ok(new { success = true, message = "EN VP GH ? VP notificado" });

                    case EstadoRequisicion.Cerrado:
                        await _emailService.EnviarCorreoCierreANominaYGhAsync(sol);
                        return Ok(new { success = true, message = "CERRADO ? Nómina y GH notificados" });

                    default:
                        await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, EstadoTitulo(sol.Estado));
                        return Ok(new { success = true, message = $"Acción aplicada, estado actual: {estadoActual?.GetDescription() ?? sol.Estado}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en accion id={Id} estado={Estado}: {Msg}", id, estado, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al aplicar acción" });
            }
        }

        [HttpPost("vpgh/enviar")]
        public async Task<IActionResult> EnviarAVpGh([FromQuery] int id)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/vpgh/enviar id={Id}", id);
            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnNomina)
                {
                    return StatusCode(409, new 
                    { 
                        success = false, 
                        message = $"La solicitud debe estar en 'EN NOMINA' para enviarse a VP GH. Estado actual: {EstadoTitulo(sol.Estado)}" 
                    });
                }

                var ok = _service.ActualizarEstadoEnVpGh(id);
                if (!ok) return StatusCode(500, new { success = false, message = "No se actualizó el estado EN VP GH" });

                var aVp = GetCorreosVpGh().ToList();
                if (aVp.Any())
                    await _emailService.EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                else
                    _logger.LogWarning("?? vpgh/enviar: no hay correos configurados en VPGH:Correo/Correos");

                return Ok(new { success = true, message = "Estado actualizado a EN VP GH y correo enviado al VP GH (con botones)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en vpgh/enviar id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpPost("vpgh/aprobar")]
        public async Task<IActionResult> AprobarVpGhCerrar([FromQuery] int id, [FromQuery] string? identificacion = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/vpgh/aprobar id={Id} usuario={User}", id, identificacion ?? "(sin id)");

            if (id <= 0)
                return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnVpGh)
                {
                    return StatusCode(409, new 
                    { 
                        success = false, 
                        message = $"Solo se puede cerrar si la solicitud está en 'EN VP GH'. Estado actual: {EstadoTitulo(sol.Estado)}" 
                    });
                }

                var ok = _service.CerrarRequisicion(id);
                if (!ok)
                    return StatusCode(500, new { success = false, message = "No se pudo cerrar la requisición" });

                await _emailService.EnviarCorreoCierreANominaYGhAsync(sol);

                return Ok(new { success = true, message = "Requisición cerrada y notificaciones enviadas (Nómina y GH por separado)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en vpgh/aprobar id={Id} usuario={User}: {Msg}", id, identificacion ?? "(sin id)", ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpPost("vpgh/rechazar")]
        public async Task<IActionResult> RechazarVpGh([FromQuery] int id, [FromQuery] string? motivo = null, [FromQuery] string? actorEmail = null, [FromQuery] string? actorNombre = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/vpgh/rechazar id={Id} actor={Actor}", id, actorEmail);

            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnVpGh)
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        message = $"Solo se puede rechazar si la solicitud está en 'EN VP GH'. Estado actual: {EstadoTitulo(sol.Estado)}"
                    });
                }

                var ok = _service.AplicarAccion(id, "RECHAZADO POR VP GH", motivo, actorEmail, actorNombre);
                if (!ok) return StatusCode(500, new { success = false, message = "No se pudo rechazar la requisición por VP GH" });

                _logger.LogInformation("? Requisición id={Id} rechazada por VP GH actor={Actor}", id, actorEmail ?? actorNombre ?? "(desconocido)");

                // Obtener solicitud actualizada
                sol = _service.ObtenerSolicitudPorId(id) ?? sol;

                // Notificar solicitante (cierre/rechazo)
                await _emailService.EnviarCorreoFinalSolicitanteAsync(sol, aprobado: false, motivoRechazo: motivo);

                // Notificar Gestión Humana
                var aGh = GetCorreosGestionHumana();
                if (aGh.Any())
                    await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "RECHAZADO POR VP GH");

                return Ok(new { success = true, message = "Requisición rechazada por VP GH y notificaciones enviadas." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en vpgh/rechazar id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al rechazar por VP GH" });
            }
        }

        [HttpPost("nomina/accion")]
        public async Task<IActionResult> NominaAccion(
            [FromQuery] int id,
            [FromQuery] string accion,
            [FromQuery] string? motivo = null,
            [FromQuery] string? actorEmail = null,
            [FromQuery] string? actorNombre = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/nomina/accion id={Id} accion={Accion} actor={Actor}", 
                id, accion, actorEmail);

            if (id <= 0 || string.IsNullOrWhiteSpace(accion))
                return BadRequest(new { success = false, message = "Parámetros inválidos" });

            var accionUpper = accion.Trim().ToUpperInvariant();
            if (accionUpper != "APROBADA" && accionUpper != "RECHAZADA")
                return BadRequest(new { success = false, message = "Acción debe ser 'APROBADA' o 'RECHAZADA'" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnNomina)
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        message = $"Solo se puede aprobar/rechazar si está en 'EN NOMINA'. Estado actual: {EstadoTitulo(sol.Estado)}"
                    });
                }

                bool ok;
                if (accionUpper == "APROBADA")
                {
                    ok = _service.AprobarNomina(id, actorNombre, actorEmail);
                    if (!ok)
                        return StatusCode(500, new { success = false, message = "No se pudo aprobar por Nómina" });

                    _logger.LogInformation("? Nómina APROBÓ id={Id} ? EN VP GH", id);

                    // Obtener solicitud actualizada para que el correo refleje el nuevo estado
                    sol = _service.ObtenerSolicitudPorId(id) ?? sol;

                    var aVp = GetCorreosVpGh().ToList();
                    if (aVp.Any())
                        await _emailService.EnviarCorreoVpGhConBotonesAsync(sol, aVp);
                    else
                        _logger.LogWarning("?? nomina/accion APROBADA: no hay correos VP GH configurados");

                    return Ok(new { success = true, message = "Nómina aprobó ? Estado actualizado a EN VP GH" });
                }
                else
                {
                    ok = _service.RechazarNomina(id, actorNombre, actorEmail, motivo);
                    if (!ok)
                        return StatusCode(500, new { success = false, message = "No se pudo rechazar por Nómina" });

                    _logger.LogInformation("? Nómina RECHAZÓ id={Id} motivo={Motivo}", id, motivo);

                    sol = _service.ObtenerSolicitudPorId(id) ?? sol;
                    await _emailService.EnviarCorreoFinalSolicitanteAsync(sol, aprobado: false, motivoRechazo: motivo);

                    var aGh = GetCorreosGestionHumana();
                    if (aGh.Any())
                        await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "RECHAZADO POR NÓMINA");

                    return Ok(new { success = true, message = "Nómina rechazó ? RECHAZADO POR NOMINA" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en nomina/accion id={Id} accion={Accion}: {Msg}", id, accion, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al procesar acción de Nómina" });
            }
        }

        [HttpGet("listar-por-aprobador")]
        public async Task<IActionResult> ListarPorAprobador([FromQuery] string correo, [FromQuery] string? estado = null)
        {
            _logger.LogInformation("?? GET /api/aprobaciones/listar-por-aprobador correo={Correo} estado={Estado}", correo, estado);

            try
            {
                if (string.IsNullOrWhiteSpace(correo))
                    return BadRequest(new { success = false, message = "Debe indicar el parámetro correo (correo del aprobador)." });

                var data = await _service.ListarPendientesPorCorreoAprobador(correo, estado);

                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("?? Sin requisiciones pendientes para correo {Correo}", correo);
                    return NotFound(new { success = false, message = "Sin pendientes para aprobar." });
                }

                _logger.LogInformation("? {Total} requisiciones encontradas para correo {Correo}", data.Count, correo);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en listar-por-aprobador correo={Correo}", correo);
                return StatusCode(500, new { success = false, message = "Error interno del servidor" });
            }
        }

        [HttpGet("listar-pendientes-vpgh")]
        public IActionResult ListarPendientesVpGh([FromQuery] string correo, [FromQuery] string? estado = null)
        {
            _logger.LogInformation("?? GET /api/aprobaciones/listar-pendientes-vpgh correo={Correo} estado={Estado}", correo, estado);

            try
            {
                if (string.IsNullOrWhiteSpace(correo))
                    return BadRequest(new { success = false, message = "Debe indicar el parámetro correo (correo de VP GH)." });

                var estadoNorm = string.IsNullOrWhiteSpace(estado)
                    ? null
                    : BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(estado);

                var data = _service.ListarDesdeConsultasPorCorreo(correo, estadoNorm);

                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("?? Sin requisiciones pendientes para VP GH {Correo}", correo);
                    return NotFound(new { success = false, message = "Sin pendientes para VP GH." });
                }

                _logger.LogInformation("? {Total} requisiciones encontradas para VP GH {Correo}", data.Count, correo);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en listar-pendientes-vpgh correo={Correo}", correo);
                return StatusCode(500, new { success = false, message = "Error interno del servidor" });
            }
        }

        [HttpPost("vpgh/devolver-a-nomina")]
        public async Task<IActionResult> DevolverAEnNomina([FromQuery] int id, [FromQuery] string? motivo = null, [FromQuery] string? actorEmail = null, [FromQuery] string? actorNombre = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/vpgh/devolver-a-nomina id={Id} actor={Actor}", id, actorEmail);

            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnVpGh)
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        message = $"Solo se puede devolver a Nómina si la solicitud está en 'EN VP GH'. Estado actual: {EstadoTitulo(sol.Estado)}"
                    });
                }

                var ok = _service.DevolverAEnNomina(id, actorNombre, actorEmail, motivo);
                if (!ok) return StatusCode(500, new { success = false, message = "No se pudo devolver a Nómina" });

                _logger.LogInformation("?? Requisición id={Id} devuelta a EN NOMINA por {Actor}", id, actorEmail ?? actorNombre ?? "(desconocido)");

                // Obtener solicitud actualizada
                sol = _service.ObtenerSolicitudPorId(id) ?? sol;

                // Notificar solicitante
                await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "EN NÓMINA");

                // Notificar destinatarios de Nómina (si están configurados)
                var aNomina = GetCorreosNomina().ToList();
                if (aNomina.Any())
                {
                    foreach (var correo in aNomina)
                    {
                        if (!string.IsNullOrWhiteSpace(correo))
                            await _emailService.EnviarCorreoAprobadorAsync(sol, correo);
                    }
                }

                // Notificar Gestión Humana con botones para revisión si aplica
                var aGh = GetCorreosGestionHumana();
                if (aGh.Any())
                    await _emailService.EnviarCorreoGhConBotonesAsync(sol, aGh, "EN NÓMINA");

                return Ok(new { success = true, message = "Requisición devuelta a EN NOMINA y notificaciones enviadas." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error devolviendo a nomina id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al devolver a Nómina" });
            }
        }

        [HttpPost("nomina/devolver-a-seleccion")]
        public async Task<IActionResult> DevolverASeleccionDesdeNomina([FromQuery] int id, [FromQuery] string? motivo = null, [FromQuery] string? actorEmail = null, [FromQuery] string? actorNombre = null)
        {
            _logger.LogInformation("??  POST /api/aprobaciones/nomina/devolver-a-seleccion id={Id} actor={Actor}", id, actorEmail);

            if (id <= 0) return BadRequest(new { success = false, message = "Id inválido" });

            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null) return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var estadoActual = sol.Estado.ToEstadoRequisicion();
                if (estadoActual != EstadoRequisicion.EnNomina)
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        message = $"Solo se puede devolver a SELECCIÓN si la solicitud está en 'EN NOMINA'. Estado actual: {EstadoTitulo(sol.Estado)}"
                    });
                }

                var ok = _service.AplicarAccion(id, "EN SELECCION", motivo, actorEmail, actorNombre);
                if (!ok) return StatusCode(500, new { success = false, message = "No se pudo devolver a SELECCIÓN" });

                _logger.LogInformation("?? Requisición id={Id} devuelta a EN SELECCIÓN por {Actor}", id, actorEmail ?? actorNombre ?? "(desconocido)");

                // Obtener solicitud actualizada
                sol = _service.ObtenerSolicitudPorId(id) ?? sol;

                // Notificar solicitante
                await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "EN SELECCION");

                // Notificar Gestión Humana
                var aGh = GetCorreosGestionHumana();
                if (aGh.Any())
                    await _emailService.EnviarCorreoGhConBotonesAsync(sol, aGh, "EN SELECCION");

                return Ok(new { success = true, message = "Requisición devuelta a EN SELECCIÓN y notificaciones enviadas." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error devolviendo a selección id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al devolver a SELECCIÓN" });
            }
        }

        private System.Collections.Generic.IEnumerable<string> GetCorreosNomina()
        {
            var one = _config["NOMINA:Correo"];
            var many = _config.GetSection("NOMINA:Correos").Get<string[]>();
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(one)) list.Add(one.Trim());
            if (many is { Length: > 0 }) list.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var res = EmailHelper.DistinctNormalizedEmails(list);
            _logger.LogInformation("NOMINA destinatarios: {To}", string.Join(", ", res));
            return res;
        }

        // Métodos auxiliares privados
        private System.Collections.Generic.IEnumerable<string> GetCorreosGestionHumana()
        {
            var arr = _config.GetSection("RRHH:CorreosRevision").Get<string[]>() ?? Array.Empty<string>();
            return EmailHelper.DistinctNormalizedEmails(arr);
        }

        private System.Collections.Generic.IEnumerable<string> GetCorreosVpGh()
        {
            var one = _config["VPGH:Correo"];
            var many = _config.GetSection("VPGH:Correos").Get<string[]>();
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(one)) list.Add(one.Trim());
            if (many is { Length: > 0 }) list.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var res = EmailHelper.DistinctNormalizedEmails(list);
            _logger.LogInformation("VPGH destinatarios: {To}", string.Join(", ", res));
            return res;
        }

        private static string EstadoTitulo(string? estado)
        {
            var estadoEnum = estado.ToEstadoRequisicion();
            
            if (!estadoEnum.HasValue)
                return string.IsNullOrWhiteSpace(estado) ? "-" : estado.Trim();

            return estadoEnum.Value switch
            {
                EstadoRequisicion.EnRevisionPorGh => "EN REVISIÓN POR GESTIÓN GH",
                EstadoRequisicion.EnAprobacion => "EN APROBACIÓN",
                EstadoRequisicion.EnSeleccion => "EN SELECCIÓN",
                _ => estadoEnum.Value.GetDescription()
            };
        }

        private static bool EsAdministrativo(BackendRequisicionPersonal.Models.SolicitudPersonal s)
        {
            var tipo = s.Tipo.ToTipoRequisicion();
            return tipo == TipoRequisicion.Administrativo;
        }
    }
}
