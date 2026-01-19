using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BackendRequisicionPersonal.Services;
using System.Threading.Tasks;

namespace BackendRequisicionPersonal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ConsultasController : ControllerBase
    {
        private readonly SolicitudesPersonalService _service;
        private readonly ILogger<ConsultasController> _logger;
        private readonly PdfService _pdfService;

        public ConsultasController(
            SolicitudesPersonalService service,
            ILogger<ConsultasController> logger,
            PdfService pdfService)
        {
            _service = service;
            _logger = logger;
            _pdfService = pdfService;
        }

        [HttpGet("listar")]
        public IActionResult Listar([FromQuery] string usuarioId, [FromQuery] string? estado = null)
        {
            _logger.LogInformation("??  GET /api/consultas/listar usuarioId={UsuarioId} estado={Estado}", usuarioId, estado);
            try
            {
                var data = _service.Listar(usuarioId);

                if (!string.IsNullOrWhiteSpace(estado))
                {
                    var estadoNorm = BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(estado);
                    data = data?
                        .Where(x => BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(x?.Estado) == estadoNorm)
                        .ToList();
                }

                if (data == null || data.Count == 0)
                {
                    _logger.LogWarning("?? Listar: sin registros para usuarioId={UsuarioId}", usuarioId);
                    return NotFound(new { success = false, message = "Sin registros" });
                }

                _logger.LogInformation("? Listar OK total={Total}", data.Count);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en listar usuarioId={UsuarioId}: {Msg}", usuarioId, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al listar" });
            }
        }

        [HttpGet("canales")]
        public IActionResult ListarCanales()
        {
            _logger.LogInformation("??  GET /api/consultas/canales");
            try
            {
                var data = _service.ListarCanales();
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en canales: {Msg}", ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpGet("cargos-canales")]
        public IActionResult ListarCargosCanales([FromQuery] string? area = null)
        {
            _logger.LogInformation("??  GET /api/consultas/cargos-canales area={Area}", area);
            try
            {
                var data = _service.ListarCargosCanales(area);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en cargos-canales area={Area}: {Msg}", area, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpGet("cargos-administrativos")]
        public IActionResult ListarCargosAdministrativos([FromQuery] string? area = null)
        {
            _logger.LogInformation("??  GET /api/consultas/cargos-administrativos area={Area}", area);
            try
            {
                var data = _service.ListarCargosAdministrativos(area);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en cargos-administrativos area={Area}: {Msg}", area, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno" });
            }
        }

        [HttpGet("pdf/{id}")]
        public IActionResult GenerarPdf(int id)
        {
            _logger.LogInformation("??  GET /api/consultas/pdf/{Id}", id);
            try
            {
                var sol = _service.ObtenerSolicitudPorId(id);
                if (sol == null)
                    return NotFound(new { success = false, message = "Solicitud no encontrada" });

                var (_, html) = _service.ConstruirCorreoRequisicion(id);
                var pdfBytes = _pdfService.HtmlToPdf(html);

                _logger.LogInformation("? PDF generado para id={Id}", id);
                return File(pdfBytes, "application/pdf", $"Requisicion_{sol.Id}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error al generar PDF para id={Id}: {Msg}", id, ex.Message);
                return StatusCode(500, new { success = false, message = "Error interno al generar PDF" });
            }
        }
    }
}
