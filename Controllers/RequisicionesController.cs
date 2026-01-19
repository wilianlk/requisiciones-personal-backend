using System;
using System.Linq;
using System.Threading.Tasks;
using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Services;
using BackendRequisicionPersonal.Services.Email;
using BackendRequisicionPersonal.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RequisicionesController : ControllerBase
    {
        private readonly SolicitudesPersonalService _service;
        private readonly ILogger<RequisicionesController> _logger;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public RequisicionesController(
            SolicitudesPersonalService service,
            ILogger<RequisicionesController> logger,
            IConfiguration config,
            IEmailService emailService)
        {
            _service = service;
            _logger = logger;
            _config = config;
            _emailService = emailService;
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

                try
                {
                    var sol = _service.ObtenerSolicitudPorId(id);
                    if (sol != null)
                    {
                        await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "EN REVISIÓN POR GESTIÓN GH");

                        var aGh = GetCorreosGestionHumana();
                        if (aGh.Any())
                            await _emailService.EnviarCorreoGhConBotonesAsync(sol, aGh, "EN REVISIÓN POR GESTIÓN GH");
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
                        // La transición al guardar seleccionado es a EN NOMINA
                        await _emailService.EnviarCorreoEstadoSolicitanteAsync(sol, "EN NÓMINA");
                    }
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "❌ Error enviando correos de GuardarSeleccionado id={Id}: {Msg}", dto.Id, exMail.Message);
                }

                return Ok(new { success = true, message = "Seleccionado guardado y enviado a Nómina." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en GuardarSeleccionado id={Id}: {Msg}", dto.Id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al guardar" });
            }
        }

        private System.Collections.Generic.IEnumerable<string> GetCorreosGestionHumana()
        {
            var arr = _config.GetSection("RRHH:CorreosRevision").Get<string[]>() ?? Array.Empty<string>();
            return EmailHelper.DistinctNormalizedEmails(arr);
        }
    }
}
