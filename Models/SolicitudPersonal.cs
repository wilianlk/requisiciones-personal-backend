namespace BackendRequisicionPersonal.Models
{
    public class SolicitudPersonal
    {
        public int Id { get; set; }

        public string IdSolicitante { get; set; }
        public string UsuarioSolicitante => IdSolicitante;
        public string Tipo { get; set; }
        public string FechaSolicitud { get; set; }
        public string Vicepresidencia { get; set; }
        public string NombreVp { get; set; }
        public string JefeInmediato { get; set; }
        public string CargoRequerido { get; set; }
        public string CentroCostos { get; set; }
        public string HorarioTrabajo { get; set; }
        public string DiasLaborales { get; set; }
        public string SalarioBasico { get; set; }
        public string TipoSolicitud { get; set; }
        public string TipoContrato { get; set; }
        public string MesesContrato { get; set; }
        public string CiudadTrabajo { get; set; }
        public string Justificacion { get; set; }
        public string CorreoJefe { get; set; }
        public string Canal { get; set; }
        public string Area { get; set; }
        public string GerenteDivision { get; set; }
        public string GerenteCanal { get; set; }
        public string TerrAsignado { get; set; }
        public string CobroAutomatico { get; set; }
        public string ZonaCiudades { get; set; }
        public string ClientesCargo { get; set; }
        public string CanalesCargo { get; set; }
        public string AuxilioMovilizacion { get; set; }
        public string SalarioGarantizado { get; set; }
        public string MesesGarantizado { get; set; }
        public string PromedioVariable { get; set; }
        public string RequiereMoto { get; set; }
        public string CorreoGerenteCanal { get; set; }
        public string CorreoGerenteDivision { get; set; }
        public string AreaSolicitante { get; set; }
        public string CargoJefeInmediato { get; set; }
        public string CentroCostosF { get; set; }
        public string HoraInicio { get; set; }
        public string HoraFin { get; set; }
        public string ActivarProcesoPor { get; set; }
        public string PersonaReemplaza { get; set; }
        public string TipoJornada { get; set; }

        public string Estado { get; set; }
        public string NivelAprobacion { get; set; }
        public string CreadoEn { get; set; }

        public string FechaEnvioAprobacion { get; set; }
        public string GhRevNombre { get; set; }
        public string GhRevCorreo { get; set; }
        public string GhRevFecha { get; set; }
        public string GhRevMotivo { get; set; }

        public string FechaAprobacion { get; set; }
        public string FechaFinAprobadores { get; set; }

        public string SalarioAsignado { get; set; }
        public string FechaIngreso { get; set; }
        public string AprobacionesIngreso { get; set; }

        public string NombreSeleccionado { get; set; }
        public string IdentificacionSeleccionado { get; set; }
        public string FechaIngresoSeleccionado { get; set; }
        public string TipoContratoSeleccionado { get; set; }

        public string Ap1Nombre { get; set; }
        public string Ap1Correo { get; set; }
        public string Ap1Estado { get; set; }
        public string Ap1Fecha { get; set; }
        public string Ap1Motivo { get; set; }

        public string Ap2Nombre { get; set; }
        public string Ap2Correo { get; set; }
        public string Ap2Estado { get; set; }
        public string Ap2Fecha { get; set; }
        public string Ap2Motivo { get; set; }

        public string Ap3Nombre { get; set; }
        public string Ap3Correo { get; set; }
        public string Ap3Estado { get; set; }
        public string Ap3Fecha { get; set; }
        public string Ap3Motivo { get; set; }

        public string VpCierreNombre { get; set; }
        public string VpCierreCorreo { get; set; }
        public string VpCierreMotivo { get; set; }   
        public string FechaCierre { get; set; }

        public string? Ap1EstadoUi { get; set; }
        public string? Ap2EstadoUi { get; set; }
        public string? Ap3EstadoUi { get; set; }
        public string? NivelActualUi { get; set; }
        public string? EstadoUi { get; set; }

        public string? SolicitanteNombre { get; set; }
        public string? SolicitanteCorreo { get; set; }

        public string? AprobadorPendienteNombre { get; set; }
        public string? AprobadorPendienteCorreo { get; set; }

        public List<Dictionary<string, string>> Aprobadores { get; set; } = new();
    }
}
