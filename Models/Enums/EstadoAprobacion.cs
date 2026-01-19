using System.ComponentModel;

namespace BackendRequisicionPersonal.Models.Enums
{
    public enum EstadoAprobacion
    {
        [Description("PENDIENTE")]
        Pendiente,

        [Description("APROBADA")]
        Aprobada,

        [Description("RECHAZADA")]
        Rechazada,

        [Description("NA")]
        NoAplica
    }
}
