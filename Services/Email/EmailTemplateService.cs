using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Models.Enums;
using BackendRequisicionPersonal.Services.Email.Models;
using System.Net;
using System.Text;

namespace BackendRequisicionPersonal.Services.Email
{
    public class EmailTemplateService
    {
        public string ShellCorreo(string titulo, string contenidoHtml)
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

        public string EncabezadoBasico(SolicitudPersonal s)
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

        public string DetallePorTipo(SolicitudPersonal s)
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

        public string TemplateCorreoInfoSolicitante(SolicitudPersonal s, string estadoTitulo)
        {
            var titulo = $"Requisición #{s.Id} — {estadoTitulo}";
            var body = EncabezadoBasico(s) + DetallePorTipo(s);
            return ShellCorreo(titulo, body);
        }

        public string TemplateCorreoFinalSolicitante(SolicitudPersonal s, bool aprobado, string? motivoRechazo)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);
            var extra = aprobado
                ? ""
                : $"<p style='margin:10px 0;color:#c53030;'>Requisición rechazada{(string.IsNullOrWhiteSpace(motivoRechazo) ? "" : $": <b>{WebUtility.HtmlEncode(motivoRechazo)}</b>")}.</p>";

            var titulo = $"Requisición #{s.Id} — {(aprobado ? "APROBADA" : "RECHAZADA")}";
            return ShellCorreo(titulo, extra + header);
        }

        public string TemplateCorreoGhConBotones(SolicitudPersonal s, string estadoTitulo, string aprobarUrl, string rechazarUrl)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

            string info = "";
            string aprobarText = "APROBAR";
            if (estadoTitulo.Contains("SELECCIÓN", StringComparison.OrdinalIgnoreCase))
            {
                info = "<p style='margin:8px 0;color:#374151;'>Acción de GH: guardar/validar el seleccionado y, si procede, <b>enviar a VP GH</b>.</p>";
                aprobarText = "GESTIONAR SELECCIONADO";
            }

            var buttons = $@"
            {info}
            <div style='margin:18px 0; display:flex; gap:12px;'>
              <a href='{WebUtility.HtmlEncode(aprobarUrl)}'
                 style='display:inline-block; padding:12px 20px; background:#16a34a; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
                 {aprobarText}
              </a>
              <a href='{WebUtility.HtmlEncode(rechazarUrl)}'
                 style='display:inline-block; padding:12px 20px; background:#dc2626; color:#fff; text-decoration:none; border-radius:6px; font-weight:bold;'>
                 RECHAZAR
              </a>
            </div>";

            var titulo = $"Requisición #{s.Id} — {estadoTitulo}";
            return ShellCorreo(titulo, header + buttons);
        }

        public string TemplateCorreoAprobador(SolicitudPersonal s, string aprobarUrl, string rechazarUrl)
        {
            var header = EncabezadoBasico(s) + @"
            <div style='margin:8px 0 4px 0;'>
              <span style='display:inline-block;background:#eef2ff;color:#3730a3;padding:6px 10px;border-radius:999px;font-size:12px;font-weight:bold;'>
                Te corresponde aprobar o rechazar esta solicitud
              </span>
            </div>" + DetallePorTipo(s);

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

        public string TemplateCorreoVpGhConBotones(SolicitudPersonal s, string aprobarUrl, string rechazarUrl)
        {
            var header = EncabezadoBasico(s) + DetallePorTipo(s);

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

        public string TemplateCorreoCierre(SolicitudPersonal s)
        {
            var titulo = $"Requisición #{s.Id} — CERRADO";
            return ShellCorreo(titulo, EncabezadoBasico(s) + DetallePorTipo(s));
        }

        public static string EstadoTitulo(string? estado)
        {
            var estadoEnum = estado.ToEstadoRequisicion();
            
            if (!estadoEnum.HasValue)
                return string.IsNullOrWhiteSpace(estado) ? "-" : estado.Trim();

            return estadoEnum.Value switch
            {
                EstadoRequisicion.EnRevisionPorGh => "EN REVISIÓN POR GESTIÓN GH", // Corrected case
                EstadoRequisicion.EnAprobacion => "EN APROBACIÓN",
                EstadoRequisicion.EnSeleccion => "EN SELECCIÓN",
                _ => estadoEnum.Value.GetDescription()
            };
        }
    }
}
