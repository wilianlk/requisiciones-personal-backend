using DinkToPdf;
using DinkToPdf.Contracts;

namespace BackendRequisicionPersonal.Services
{
    public class PdfService
    {
        private readonly IConverter _converter;

        public PdfService(IConverter converter)
        {
            _converter = converter;
        }

        public byte[] HtmlToPdf(string htmlContent)
        {
            var doc = new HtmlToPdfDocument()
            {
                GlobalSettings = {
                    ColorMode = ColorMode.Color,
                    Orientation = Orientation.Portrait,
                    PaperSize = PaperKind.A4,
                    Margins = new MarginSettings {
                        Top = 10,
                        Bottom = 10,
                        Left = 10,
                        Right = 10
                    }
                },
                Objects = {
                    new ObjectSettings() {
                        HtmlContent = htmlContent,
                        WebSettings = {
                            DefaultEncoding = "utf-8"
                        },
                        LoadSettings = {
                            BlockLocalFileAccess = false
                        }
                    }
                }
            };

            return _converter.Convert(doc);
        }
    }
}
