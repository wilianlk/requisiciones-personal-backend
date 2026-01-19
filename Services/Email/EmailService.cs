using BackendRequisicionPersonal.Helpers;
using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace BackendRequisicionPersonal.Services.Email
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly SmtpSettings _smtp;
        private readonly IConfiguration _config;
        private readonly PdfService _pdfService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly EmailTemplateService _templateService;
        private readonly List<string> _ccPowerApps;

        public EmailService(
            ILogger<EmailService> _logger,
            Microsoft.Extensions.Options.IOptions<SmtpSettings> smtpOptions,
            IConfiguration config,
            PdfService pdfService,
            IHttpContextAccessor httpContextAccessor,
            IWebHostEnvironment env,
            EmailTemplateService templateService)
        {
            this._logger = _logger;
            _smtp = smtpOptions.Value;
            _config = config;
            _pdfService = pdfService;
            _httpContextAccessor = httpContextAccessor;
            _env = env;
            _templateService = templateService;

            // Cargar CC de PowerApps
            try
            {
                var ccOne = _config["PowerApps:CC"];
                var ccMany = _config.GetSection("PowerApps:CC").Get<string[]>();

                var cc = new List<string>();
                if (!string.IsNullOrWhiteSpace(ccOne)) cc.Add(ccOne.Trim());
                if (ccMany is { Length: > 0 })
                    cc.AddRange(ccMany.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

                _ccPowerApps = EmailHelper.DistinctNormalizedEmails(cc);
                _logger.LogInformation("PowerApps CC cargado: {CC}", string.Join(", ", _ccPowerApps));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo cargar PowerApps:CC");
                _ccPowerApps = new List<string>();
            }
        }

        public async Task EnviarCorreoEstadoSolicitanteAsync(SolicitudPersonal sol, string estadoTitulo)
        {
            try
            {
                var destinatarios = GetDestinatariosSolicitante(sol);
                var estadoUpper = estadoTitulo.Trim().ToUpper();
                bool adjuntarPdf = estadoUpper == "EN REVISIÓN POR GESTIÓN GH" || 
                                   estadoUpper == "EN NÓMINA" ||
                                   estadoUpper == "EN NOMINA";

                foreach (var to in destinatarios)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BuildBaseMail(
                        sol,
                        $"Requisición #{sol.Id} — {estadoTitulo}",
                        _templateService.TemplateCorreoInfoSolicitante(sol, estadoTitulo),
                        adjuntarPdf);

                    mail.To.Add(to);
                    AddCcPowerApps(mail);

                    await smtp.SendMailAsync(mail);

                    _logger.LogInformation(
                        "?? Solicitante notificado ({Estado}) a {To} (adjuntoPdf={Adj}) (id={Id})",
                        estadoTitulo, to, adjuntarPdf, sol.Id
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error correo solicitante ({Estado}) id={Id}: {Msg}",
                    estadoTitulo, sol.Id, ex.Message);
            }
        }

        public async Task EnviarCorreoFinalSolicitanteAsync(SolicitudPersonal sol, bool aprobado, string? motivoRechazo = null)
        {
            try
            {
                var destinatarios = GetDestinatariosSolicitante(sol);
                
                foreach (var to in destinatarios)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BuildBaseMail(
                        sol,
                        aprobado ? $"Requisición #{sol.Id} — APROBADA" : $"Requisición #{sol.Id} — RECHAZADA",
                        _templateService.TemplateCorreoFinalSolicitante(sol, aprobado, motivoRechazo));
                    
                    mail.To.Add(to);
                    AddCcPowerApps(mail);
                    
                    await smtp.SendMailAsync(mail);
                    
                    _logger.LogInformation("?? Correo Final a solicitante ({Estado}) {To} (id={Id})",
                        aprobado ? "APROBADA" : "RECHAZADA", to, sol.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error correo final solicitante id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }

        public async Task EnviarCorreoGhConBotonesAsync(SolicitudPersonal sol, IEnumerable<string> destinatariosGh, string estadoTitulo)
        {
            try
            {
                var request = _httpContextAccessor.HttpContext?.Request;
                if (request == null)
                {
                    _logger.LogWarning("No hay HttpContext disponible para construir URLs");
                    return;
                }

                string aprobarUrl;
                if (estadoTitulo.Contains("REVISIÓN", StringComparison.OrdinalIgnoreCase))
                {
                    aprobarUrl = UrlHelper.BuildGetUrl(request, $"/api/aprobaciones/revisado-rrhh?id={sol.Id}");
                }
                else if (estadoTitulo.Contains("SELECCIÓN", StringComparison.OrdinalIgnoreCase))
                {
                    aprobarUrl = UrlHelper.BuildFrontGestionSeleccionUrl(_config, _httpContextAccessor.HttpContext, sol.Id);
                }
                else
                {
                    aprobarUrl = UrlHelper.BuildAccionUrl(request, sol.Id, "APROBADA");
                }

                var rechazarUrl = UrlHelper.BuildFrontRechazoUrl(_config, _httpContextAccessor.HttpContext, sol.Id);

                using var smtp = BuildSmtp();
                using var mail = BuildBaseMail(
                    sol,
                    $"Requisición #{sol.Id} — {estadoTitulo}",
                    _templateService.TemplateCorreoGhConBotones(sol, estadoTitulo, aprobarUrl, rechazarUrl));
                
                foreach (var to in destinatariosGh)
                    mail.To.Add(to);
                
                AddCcPowerApps(mail);
                
                await smtp.SendMailAsync(mail);
                
                _logger.LogInformation("?? GH notificado con botones ({Estado}) a: {To} (id={Id})",
                    estadoTitulo, string.Join(", ", mail.To.Select(t => t.Address)), sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error correo GH con botones ({Estado}) id={Id}: {Msg}", estadoTitulo, sol.Id, ex.Message);
            }
        }

        public async Task EnviarCorreoAprobadorAsync(SolicitudPersonal sol, string aprobadorEmail)
        {
            try
            {
                var request = _httpContextAccessor.HttpContext?.Request;
                if (request == null)
                {
                    _logger.LogWarning("No hay HttpContext disponible para construir URLs");
                    return;
                }

                var aprobarUrl = UrlHelper.BuildAccionUrl(request, sol.Id, "APROBADA", aprobadorEmail);

                // Determinar el nivel del aprobador
                var nivel = 0;
                if (!string.IsNullOrWhiteSpace(aprobadorEmail))
                {
                    if (string.Equals(sol.Ap1Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 1;
                    else if (string.Equals(sol.Ap2Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 2;
                    else if (string.Equals(sol.Ap3Correo?.Trim(), aprobadorEmail.Trim(), StringComparison.OrdinalIgnoreCase)) nivel = 3;
                }

                var rechazarUrl = UrlHelper.BuildFrontRechazoUrl(_config, _httpContextAccessor.HttpContext, sol.Id, aprobadorEmail, nivel);

                using var smtp = BuildSmtp();
                using var mail = BuildBaseMail(
                    sol,
                    $"Requisición #{sol.Id} — {EmailTemplateService.EstadoTitulo(sol.Estado)}",
                    _templateService.TemplateCorreoAprobador(sol, aprobarUrl, rechazarUrl));
                
                mail.To.Add(aprobadorEmail);
                AddCcPowerApps(mail);
                
                await smtp.SendMailAsync(mail);
                
                _logger.LogInformation("?? Correo Aprobador enviado a {To} (id={Id})", aprobadorEmail, sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error enviando correo Aprobador a {To} (id={Id}): {Msg}", aprobadorEmail, sol.Id, ex.Message);
            }
        }

        public async Task EnviarCorreoVpGhConBotonesAsync(SolicitudPersonal sol, IEnumerable<string> destinatariosVpGh)
        {
            try
            {
                var toList = EmailHelper.DistinctNormalizedEmails(destinatariosVpGh);
                if (toList.Count == 0)
                {
                    _logger.LogWarning("VP GH: no hay destinatarios para enviar (id={Id}).", sol.Id);
                    return;
                }

                var request = _httpContextAccessor.HttpContext?.Request;
                if (request == null)
                {
                    _logger.LogWarning("No hay HttpContext disponible para construir URLs");
                    return;
                }

                var aprobarUrl = UrlHelper.BuildAccionUrl(request, sol.Id, "APROBADO POR VP GH");
                var rechazarUrl = UrlHelper.BuildFrontRechazoUrl(_config, _httpContextAccessor.HttpContext, sol.Id) + "&vpgh=true";

                using var smtp = BuildSmtp();
                using var mail = BuildBaseMail(
                    sol,
                    $"Requisición #{sol.Id} — EN VP GH",
                    _templateService.TemplateCorreoVpGhConBotones(sol, aprobarUrl, rechazarUrl));

                foreach (var to in toList) mail.To.Add(to);
                AddCcPowerApps(mail);

                await smtp.SendMailAsync(mail);
                
                _logger.LogInformation("?? Correo VP GH (botones) a {To} (id={Id})", string.Join(", ", toList), sol.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error correo VP GH (id={Id}): {Msg}", sol.Id, ex.Message);
            }
        }

        public async Task EnviarCorreoCierreANominaYGhAsync(SolicitudPersonal sol)
        {
            try
            {
                var nomina = GetCorreosNomina().ToList();
                var gh = GetCorreosGestionHumana().ToList();

                // 1) Nómina
                if (nomina.Any())
                {
                    using var smtp1 = BuildSmtp();
                    using var mail1 = BuildBaseMail(
                        sol, 
                        $"Requisición #{sol.Id} — CERRADO", 
                        _templateService.TemplateCorreoCierre(sol));
                    
                    foreach (var to in nomina) mail1.To.Add(to);
                    AddCcPowerApps(mail1);
                    
                    await smtp1.SendMailAsync(mail1);
                    
                    _logger.LogInformation("?? Cierre enviado a Nómina ({To}) id={Id}", string.Join(", ", nomina), sol.Id);
                }
                else
                {
                    _logger.LogWarning("?? Cierre: no hay correos configurados en Nomina:Correo/Correos");
                }

                // 2) GH
                if (gh.Any())
                {
                    using var smtp2 = BuildSmtp();
                    using var mail2 = BuildBaseMail(
                        sol, 
                        $"Requisición #{sol.Id} — CERRADO", 
                        _templateService.TemplateCorreoCierre(sol));
                    
                    foreach (var to in gh) mail2.To.Add(to);
                    AddCcPowerApps(mail2);
                    
                    await smtp2.SendMailAsync(mail2);
                    
                    _logger.LogInformation("?? Cierre enviado a GH ({To}) id={Id}", string.Join(", ", gh), sol.Id);
                }
                else
                {
                    _logger.LogWarning("?? Cierre: no hay correos configurados en RRHH:CorreosRevision");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error correos de Cierre id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }

        public async Task NotificarSolicitanteSinDuplicarAprobadoresAsync(
            SolicitudPersonal sol, 
            string estadoTitulo, 
            IEnumerable<string> aprobadoresRaw)
        {
            try
            {
                var aprobadores = EmailHelper.DistinctNormalizedEmails(aprobadoresRaw ?? Array.Empty<string>());
                var solicitantes = EmailHelper.DistinctNormalizedEmails(GetDestinatariosSolicitante(sol));
                var soloSolicitantes = solicitantes.Except(aprobadores).ToList();

                if (soloSolicitantes.Count == 0)
                {
                    _logger.LogInformation("?? Aviso solicitante omitido por coincidencia con aprobador (id={Id})", sol.Id);
                    return;
                }

                foreach (var to in soloSolicitantes)
                {
                    using var smtp = BuildSmtp();
                    using var mail = BuildBaseMail(
                        sol,
                        $"Requisición #{sol.Id} — {estadoTitulo}",
                        _templateService.TemplateCorreoInfoSolicitante(sol, estadoTitulo));
                    
                    mail.To.Add(to);
                    AddCcPowerApps(mail);
                    
                    await smtp.SendMailAsync(mail);
                    
                    _logger.LogInformation("?? Solicitante notificado ({Estado}) a {To} (id={Id})", estadoTitulo, to, sol.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en NotificarSolicitanteSinDuplicarAprobadoresAsync id={Id}: {Msg}", sol.Id, ex.Message);
            }
        }

        // ========== Métodos privados ==========

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

        private IEnumerable<string> GetDestinatariosSolicitante(SolicitudPersonal sol)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(sol.CorreoJefe))
                list.Add(sol.CorreoJefe);
            return EmailHelper.DistinctNormalizedEmails(list);
        }

        private IEnumerable<string> GetCorreosGestionHumana()
        {
            var arr = _config.GetSection("RRHH:CorreosRevision").Get<string[]>() ?? Array.Empty<string>();
            return EmailHelper.DistinctNormalizedEmails(arr);
        }

        private IEnumerable<string> GetCorreosNomina()
        {
            var one = _config["Nomina:Correo"];
            var many = _config.GetSection("Nomina:Correos").Get<string[]>();
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(one)) list.Add(one.Trim());
            if (many is { Length: > 0 }) list.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var res = EmailHelper.DistinctNormalizedEmails(list);
            _logger.LogInformation("Nómina destinatarios: {To}", string.Join(", ", res));
            return res;
        }

        private MailMessage BuildBaseMail(
            SolicitudPersonal s,
            string subject,
            string htmlBody,
            bool adjuntarPdf = false)
        {
            var mail = new MailMessage
            {
                From = new MailAddress(_smtp.User, "Requisición de Personal"),
                Subject = subject,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };

            var request = _httpContextAccessor.HttpContext?.Request;
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
                    TransferEncoding = TransferEncoding.Base64
                };

                htmlView.LinkedResources.Add(logo);
            }

            mail.AlternateViews.Add(htmlView);

            if (adjuntarPdf)
            {
                string estadoTitulo = EmailTemplateService.EstadoTitulo(s.Estado);
                string titulo = $"Requisición #{s.Id} — {estadoTitulo}";

                string contenido = _templateService.EncabezadoBasico(s) + _templateService.DetallePorTipo(s);
                string htmlCompleto = _templateService.ShellCorreo(titulo, contenido);

                var pdfBytes = _pdfService.HtmlToPdf(htmlCompleto);

                var stream = new MemoryStream(pdfBytes);
                mail.Attachments.Add(new Attachment(stream, $"Requisicion_{s.Id}.pdf", "application/pdf"));
            }

            return mail;
        }
    }
}
