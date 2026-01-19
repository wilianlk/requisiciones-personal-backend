using System;
using System.Text;
using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Models.Enums;
using IBM.Data.Db2;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Services
{
    public class ReportesService
    {
        private readonly string _connectionString;
        private readonly ILogger<ReportesService> _logger;

        public ReportesService(
            ILogger<ReportesService> logger,
            string connectionString)
        {
            _logger = logger;
            _connectionString = connectionString;
        }

        public (string Asunto, string Html) ConstruirCorreoRequisicion(int id)
        {
            try
            {
                var sol = ObtenerSolicitudPorId(id);
                if (sol == null)
                    return ("", "");

                var estadoEnum = sol.Estado.ToEstadoRequisicion();
                var estadoTitulo = estadoEnum.HasValue ?
                    (estadoEnum.Value == EstadoRequisicion.EnRevisionPorGh ? "EN REVISIÓN POR GESTIÓN GH" :
                     estadoEnum.Value == EstadoRequisicion.EnAprobacion ? "EN APROBACIÓN" :
                     estadoEnum.Value == EstadoRequisicion.EnSeleccion ? "EN SELECCIÓN" :
                     estadoEnum.Value.GetDescription()) :
                    (string.IsNullOrWhiteSpace(sol.Estado) ? "-" : sol.Estado.Trim());

                var asunto = $"Requisición #{sol.Id} — {estadoTitulo}";

                var html = $@"
                <html>
                  <body style='margin:0;padding:16px;background:#f3f4f6;font-family:Arial, Helvetica, sans-serif;'>
                    <div style='max-width:840px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden;'>
                      <div style='background:#0b4f79;color:#ffffff;padding:16px 20px;'>
                        <h2 style='margin:0;font-size:18px;'>{System.Net.WebUtility.HtmlEncode(asunto)}</h2>
                      </div>
                      <div style='padding:16px 20px;'>
                        {EncabezadoBasicoHtml(sol)}
                        {DetallePorTipoHtml(sol)}
                      </div>
                    </div>
                  </body>
                </html>";

                return (asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ConstruirCorreoRequisicion id={Id}", id);
                return ("", "");
            }
        }

        private SolicitudPersonal? ObtenerSolicitudPorId(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT
                      s.id,
                      s.id_solicitante,
                      r.jefe_inmediato_solicitante AS solicitante_nombre,
                      r.e_mail AS solicitante_correo,
                      s.tipo, s.fecha_solicitud, s.vicepresidencia, s.nombre_vp,
                      s.jefe_inmediato, s.cargo_requerido, s.centro_costos, s.horario_trabajo, s.dias_laborales, s.salario_basico,
                      s.tipo_solicitud, s.tipo_contrato, s.meses_contrato, s.ciudad_trabajo, s.justificacion, s.correo_jefe,
                      s.canal, s.area, s.gerente_division, s.gerente_canal, s.terr_asignado, s.cobro_automatico, s.zona_ciudades,
                      s.clientes_cargo, s.canales_cargo, s.auxilio_movilizacion, s.salario_garantizado, s.meses_garantizado, s.promedio_variable,
                      s.requiere_moto, s.correo_gerente_canal, s.correo_gerente_division, s.area_solicitante, s.cargo_jefe_inmediato,
                      s.centro_costos_f, s.hora_inicio, s.hora_fin, s.activar_proceso_por, s.persona_reemplaza, s.tipo_jornada,
                      s.estado, s.nivel_aprobacion, s.creado_en,
                      s.fecha_envio_aprobacion, s.gh_rev_nombre, s.gh_rev_correo, s.gh_rev_fecha, s.gh_rev_motivo,
                      s.fecha_aprobacion, s.fecha_fin_aprobadores,
                      s.salario_asignado, s.fecha_ingreso, s.aprobaciones_ingreso,
                      s.nombre_seleccionado, s.identificacion_seleccionado, s.fecha_ingreso_seleccionado, s.tipo_contrato_seleccionado,
                      s.ap1_nombre, s.ap1_correo, s.ap1_estado, s.ap1_fecha, s.ap1_motivo,
                      s.ap2_nombre, s.ap2_correo, s.ap2_estado, s.ap2_fecha, s.ap2_motivo,
                      s.ap3_nombre, s.ap3_correo, s.ap3_estado, s.ap3_fecha, s.ap3_motivo,
                      s.vp_cierre_nombre, s.vp_cierre_correo, s.vp_cierre_motivo, s.fecha_cierre
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    WHERE s.id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                static string? S(DB2DataReader rd, int i) =>
                    (i < rd.FieldCount && !rd.IsDBNull(i)) ? rd.GetString(i).Trim() : null;

                return new SolicitudPersonal
                {
                    Id = r.GetInt32(0),
                    IdSolicitante = S(r, 1),
                    SolicitanteNombre = S(r, 2),
                    SolicitanteCorreo = S(r, 3),
                    Tipo = S(r, 4),
                    FechaSolicitud = S(r, 5),
                    Vicepresidencia = S(r, 6),
                    NombreVp = S(r, 7),
                    JefeInmediato = S(r, 8),
                    CargoRequerido = S(r, 9),
                    CentroCostos = S(r, 10),
                    HorarioTrabajo = S(r, 11),
                    DiasLaborales = S(r, 12),
                    SalarioBasico = S(r, 13),
                    TipoSolicitud = S(r, 14),
                    TipoContrato = S(r, 15),
                    MesesContrato = S(r, 16),
                    CiudadTrabajo = S(r, 17),
                    Justificacion = S(r, 18),
                    CorreoJefe = S(r, 19),
                    Canal = S(r, 20),
                    Area = S(r, 21),
                    GerenteDivision = S(r, 22),
                    GerenteCanal = S(r, 23),
                    TerrAsignado = S(r, 24),
                    CobroAutomatico = S(r, 25),
                    ZonaCiudades = S(r, 26),
                    ClientesCargo = S(r, 27),
                    CanalesCargo = S(r, 28),
                    AuxilioMovilizacion = S(r, 29),
                    SalarioGarantizado = S(r, 30),
                    MesesGarantizado = S(r, 31),
                    PromedioVariable = S(r, 32),
                    RequiereMoto = S(r, 33),
                    CorreoGerenteCanal = S(r, 34),
                    CorreoGerenteDivision = S(r, 35),
                    AreaSolicitante = S(r, 36),
                    CargoJefeInmediato = S(r, 37),
                    CentroCostosF = S(r, 38),
                    HoraInicio = S(r, 39),
                    HoraFin = S(r, 40),
                    ActivarProcesoPor = S(r, 41),
                    PersonaReemplaza = S(r, 42),
                    TipoJornada = S(r, 43),
                    Estado = S(r, 44),
                    NivelAprobacion = S(r, 45),
                    CreadoEn = S(r, 46),
                    FechaEnvioAprobacion = S(r, 47),
                    GhRevNombre = S(r, 48),
                    GhRevCorreo = S(r, 49),
                    GhRevFecha = S(r, 50),
                    GhRevMotivo = S(r, 51),
                    FechaAprobacion = S(r, 52),
                    FechaFinAprobadores = S(r, 53),
                    SalarioAsignado = S(r, 54),
                    FechaIngreso = S(r, 55),
                    AprobacionesIngreso = S(r, 56),
                    NombreSeleccionado = S(r, 57),
                    IdentificacionSeleccionado = S(r, 58),
                    FechaIngresoSeleccionado = S(r, 59),
                    TipoContratoSeleccionado = S(r, 60),
                    Ap1Nombre = S(r, 61),
                    Ap1Correo = S(r, 62),
                    Ap1Estado = S(r, 63),
                    Ap1Fecha = S(r, 64),
                    Ap1Motivo = S(r, 65),
                    Ap2Nombre = S(r, 66),
                    Ap2Correo = S(r, 67),
                    Ap2Estado = S(r, 68),
                    Ap2Fecha = S(r, 69),
                    Ap2Motivo = S(r, 70),
                    Ap3Nombre = S(r, 71),
                    Ap3Correo = S(r, 72),
                    Ap3Estado = S(r, 73),
                    Ap3Fecha = S(r, 74),
                    Ap3Motivo = S(r, 75),
                    VpCierreNombre = S(r, 76),
                    VpCierreCorreo = S(r, 77),
                    VpCierreMotivo = S(r, 78),
                    FechaCierre = S(r, 79)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerSolicitudPorId id={Id}", id);
                return null;
            }
        }

        private string EncabezadoBasicoHtml(SolicitudPersonal s)
        {
            static string H(string? v) => System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim());
            string K(string v) => $"<td style='padding:6px 10px;border:1px solid #d1d5db;width:35%;font-weight:bold;background:#f9fafb;'>{System.Net.WebUtility.HtmlEncode(v)}</td>";
            string V(string? v) => $"<td style='padding:6px 10px;border:1px solid #d1d5db;'>{H(v)}</td>";
            string Row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var sb = new StringBuilder();
            sb.Append("<table style='width:100%;border-collapse:collapse;border:1px solid #d1d5db;'>");
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

        private string DetallePorTipoHtml(SolicitudPersonal s)
        {
            static string H(string? v) => System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(v) ? "-" : v.Trim());
            string K(string v) => $"<td style='padding:6px 10px;border:1px solid #d1d5db;width:35%;font-weight:bold;background:#f9fafb;'>{System.Net.WebUtility.HtmlEncode(v)}</td>";
            string V(string? v) => $"<td style='padding:6px 10px;border:1px solid #d1d5db;'>{H(v)}</td>";
            string Row(string k, string? v) => $"<tr>{K(k)}{V(v)}</tr>";

            var tipo = (s.Tipo ?? "").Trim().ToUpperInvariant();
            var sb = new StringBuilder();

            sb.Append("<h3 style='margin:14px 0 8px 0;'>Detalle de la solicitud</h3>");
            sb.Append("<table style='width:100%;border-collapse:collapse;border:1px solid #d1d5db;'>");

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
    }
}
