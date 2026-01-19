using BackendRequisicionPersonal.Models;
using BackendRequisicionPersonal.Models.Enums;

namespace BackendRequisicionPersonal.Examples
{
    public static class EnumUsageExamples
    {
        public static void EjemploConversionBD()
        {
            string estadoBD = "EN APROBACION";
            string tipoBD = "COMERCIAL";
            string nivelBD = "1";
            string ap1EstadoBD = "PENDIENTE";

            EstadoRequisicion? estado = estadoBD.ToEstadoRequisicion();
            TipoRequisicion? tipo = tipoBD.ToTipoRequisicion();
            NivelAprobacion? nivel = nivelBD.ToNivelAprobacion();
            EstadoAprobacion? ap1Estado = ap1EstadoBD.ToEstadoAprobacion();

            if (estado.HasValue && tipo.HasValue)
            {
                if (estado.Value == EstadoRequisicion.EnAprobacion)
                {
                }

                if (tipo.Value == TipoRequisicion.Comercial)
                {
                }
            }
        }

        public static bool ValidarEstadoParaAprobacion(SolicitudPersonal solicitud)
        {
            var estado = solicitud.Estado.ToEstadoRequisicion();

            if (!estado.HasValue)
                return false;

            return estado.Value switch
            {
                EstadoRequisicion.EnAprobacion => true,
                EstadoRequisicion.EnRevisionPorGh => true, // Corrected case
                EstadoRequisicion.EnSeleccion => true,
                EstadoRequisicion.EnVpGh => true, // Corrected case
                _ => false
            };
        }

        public static bool PuedeTransicionarEstado(SolicitudPersonal solicitud)
        {
            var estadoActual = solicitud.Estado.ToEstadoRequisicion();

            if (!estadoActual.HasValue)
                return false;

            return !estadoActual.Value.EsEstadoFinal();
        }

        public static int ObtenerNumeroAprobadoresRequeridos(SolicitudPersonal solicitud)
        {
            var tipo = solicitud.Tipo.ToTipoRequisicion();

            return tipo switch
            {
                TipoRequisicion.Administrativo => 1,
                TipoRequisicion.Comercial => 2,
                _ => 0
            };
        }

        public static NivelAprobacion ObtenerSiguienteNivel(string nivelActual)
        {
            var nivel = nivelActual.ToNivelAprobacion();

            return nivel switch
            {
                NivelAprobacion.Nivel1 => NivelAprobacion.Nivel2,
                NivelAprobacion.Nivel2 => NivelAprobacion.Nivel3,
                NivelAprobacion.Nivel3 => NivelAprobacion.Final,
                _ => NivelAprobacion.Final
            };
        }

        public static void EjemploGuardarEnBD()
        {
            EstadoRequisicion nuevoEstado = EstadoRequisicion.EnAprobacion;

            string estadoParaBD = nuevoEstado.GetDescription();
        }

        public static bool ValidarEstadoRecibido(string estadoRecibido)
        {
            var estado = estadoRecibido.ToEstadoRequisicion();

            if (!estado.HasValue)
            {
                return false;
            }

            var permitidos = new[]
            {
                EstadoRequisicion.EnAprobacion,
                EstadoRequisicion.EnSeleccion,
                EstadoRequisicion.EnVpGh // Corrected case
            };

            return permitidos.Contains(estado.Value);
        }

        public static string ObtenerMensajePorEstado(SolicitudPersonal solicitud)
        {
            var estado = solicitud.Estado.ToEstadoRequisicion();

            return estado switch
            {
                EstadoRequisicion.EnRevisionPorGh => "Tu solicitud está siendo revisada por Gestión Humana", // Corrected case
                EstadoRequisicion.EnAprobacion => "Tu solicitud está en proceso de aprobación",
                EstadoRequisicion.EnSeleccion => "Tu solicitud fue aprobada y está en proceso de selección",
                EstadoRequisicion.EnVpGh => "Tu solicitud está siendo revisada por el VP de Gestión Humana",
                EstadoRequisicion.Aprobada => "¡Felicidades! Tu solicitud ha sido aprobada",
                EstadoRequisicion.Cerrado => "Tu solicitud ha sido cerrada exitosamente",
                _ when estado?.EsRechazado() == true => "Lo sentimos, tu solicitud fue rechazada",
                _ => "Estado desconocido"
            };
        }

        public static bool TodosAprobadoresCompletados(SolicitudPersonal solicitud)
        {
            var ap1 = solicitud.Ap1Estado.ToEstadoAprobacion();
            var ap2 = solicitud.Ap2Estado.ToEstadoAprobacion();
            var ap3 = solicitud.Ap3Estado.ToEstadoAprobacion();

            bool ap1Ok = !ap1.HasValue || 
                         ap1 == EstadoAprobacion.NoAplica || 
                         ap1 == EstadoAprobacion.Aprobada;

            bool ap2Ok = !ap2.HasValue || 
                         ap2 == EstadoAprobacion.NoAplica || 
                         ap2 == EstadoAprobacion.Aprobada;

            bool ap3Ok = !ap3.HasValue || 
                         ap3 == EstadoAprobacion.NoAplica || 
                         ap3 == EstadoAprobacion.Aprobada;

            return ap1Ok && ap2Ok && ap3Ok;
        }

        public static string[] ObtenerEstadosValidosParaAPI()
        {
            return EnumExtensions.GetEstadosValidos();
        }

        public static bool RequiereNotificacionUrgente(SolicitudPersonal solicitud)
        {
            var estado = solicitud.Estado.ToEstadoRequisicion();
            var tipo = solicitud.Tipo.ToTipoRequisicion();

            return (estado, tipo) switch
            {
                (EstadoRequisicion.EnVpGh, TipoRequisicion.Comercial) => true, // Corrected case
                (EstadoRequisicion.RechazadoPorVpGh, _) => true,
                (EstadoRequisicion.Cerrado, _) => true,
                _ => false
            };
        }

        public static class EstadoHelper
        {
            public static bool EsEstadoActivo(string? estado)
            {
                var enumEstado = estado.ToEstadoRequisicion();
                if (!enumEstado.HasValue) return false;

                return !enumEstado.Value.EsEstadoFinal();
            }

            public static bool PuedeSerEditado(string? estado)
            {
                var enumEstado = estado.ToEstadoRequisicion();
                if (!enumEstado.HasValue) return false;

                return enumEstado.Value == EstadoRequisicion.EnRevisionPorGh; // Corrected case
            }

            public static bool RequiereAprobacion(string? estado)
            {
                var enumEstado = estado.ToEstadoRequisicion();
                if (!enumEstado.HasValue) return false;

                return enumEstado.Value switch
                {
                    EstadoRequisicion.EnAprobacion => true,
                    EstadoRequisicion.EnSeleccion => true,
                    EstadoRequisicion.EnVpGh => true, // Corrected case
                    _ => false
                };
            }
        }
    }
}
