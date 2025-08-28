namespace BackendRequisicionPersonal.Models.Auth
{
    public class UsuarioInfo
    {
        public string? Identificacion { get; set; }
        public string? JefeInmediatoSolicitante { get; set; }
        public string? Cargo { get; set; }
        public string? Correo { get; set; }
        public string? Canal { get; set; }
        public string? Area { get; set; }
        public string? Aprobador1 { get; set; }
        public string? CorreoAprobador1 { get; set; }
        public string? Aprobador2 { get; set; }
        public string? CorreoAprobador2 { get; set; }
        public string? Aprobador3 { get; set; }
        public string? CorreoAprobador3 { get; set; }
        public string? CentroCosto { get; set; }
        public string? Vp { get; set; }

        public List<string> Roles { get; set; } = new();
    }
}
