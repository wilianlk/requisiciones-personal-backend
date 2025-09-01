namespace BackendRequisicionPersonal.Models
{
    public class SeleccionadoDto
    {
        public int Id { get; set; }
        public string AprobacionesIngreso { get; set; }
        public string NombreSeleccionado { get; set; }
        public string IdentificacionSeleccionado { get; set; }
        public System.DateTime? FechaIngresoSeleccionado { get; set; }
        public string? TipoContratoSeleccionado { get; set; }
    }
}
