#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BackendRequisicionPersonal.Models
{
    public class SolicitudPersonalDto
    {
        [Required] public string Tipo { get; set; } = default!;
        [Required] public string FechaSolicitud { get; set; } = default!;
        [Required] public string Vicepresidencia { get; set; } = default!;
        [Required] public string JefeInmediato { get; set; } = default!;
        [Required] public string CargoRequerido { get; set; } = default!;
        public string? CentroCostos { get; set; }
        [Required] public string DiasLaborales { get; set; } = default!;
        [Required] public string SalarioBasico { get; set; } = default!;
        [Required] public string TipoSolicitud { get; set; } = default!;
        [Required] public string TipoContrato { get; set; } = default!;
        [Required] public string CiudadTrabajo { get; set; } = default!;
        [Required] public string Justificacion { get; set; } = default!;
        [Required, EmailAddress] public string CorreoJefe { get; set; } = default!;

        public string? IdSolicitante { get; set; }
        public string? NombreVp { get; set; }
        public string? HorarioTrabajo { get; set; }
        public string? MesesContrato { get; set; }
        public string? Canal { get; set; }
        public string? Area { get; set; }
        public string? GerenteDivision { get; set; }
        public string? GerenteCanal { get; set; }
        public string? TerrAsignado { get; set; }
        public string? CobroAutomatico { get; set; }
        public string? ZonaCiudades { get; set; }
        public string? ClientesCargo { get; set; }
        public string? CanalesCargo { get; set; }
        public string? AuxilioMovilizacion { get; set; }
        public string? SalarioGarantizado { get; set; }
        public string? MesesGarantizado { get; set; }
        public string? PromedioVariable { get; set; }
        public bool RequiereMoto { get; set; }
        public string? CorreoGerenteCanal { get; set; }
        public string? CorreoGerenteDivision { get; set; }
        public string? AreaSolicitante { get; set; }
        public string? CargoJefeInmediato { get; set; }
        public string? CentroCostosF { get; set; }
        public string? HoraInicio { get; set; }
        public string? HoraFin { get; set; }
        public string? ActivarProcesoPor { get; set; }
        public string? PersonaReemplaza { get; set; }
        public string? TipoJornada { get; set; }

        public string? Estado { get; set; }
        public string? CreadoEn { get; set; }
        public string? FechaEnvioAprobacion { get; set; }
        public string? FechaAprobacion { get; set; }

        public string? SalarioAsignado { get; set; }
        public string? FechaIngreso { get; set; }
        public string? AprobacionesIngreso { get; set; }
    }
}
