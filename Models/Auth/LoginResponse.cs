namespace BackendRequisicionPersonal.Models.Auth
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public UsuarioInfo Usuario { get; set; }
    }
}
