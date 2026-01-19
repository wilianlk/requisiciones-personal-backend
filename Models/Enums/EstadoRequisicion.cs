using System.ComponentModel;

namespace BackendRequisicionPersonal.Models.Enums
{
    public enum EstadoRequisicion
    {
        [Description("EN REVISION POR GH")]
        EnRevisionPorGh,

        [Description("APROBADO POR RRHH")]
        AprobadoPorRrhh,

        [Description("EN APROBACION")]
        EnAprobacion,

        [Description("APROBADO POR APROBADOR")]
        AprobadoPorAprobador,

        [Description("EN SELECCION")]
        EnSeleccion,

        [Description("APROBADO POR SELECCION")]
        AprobadoPorSeleccion,

        [Description("EN NOMINA")]
        EnNomina,

        [Description("APROBADO POR NOMINA")]
        AprobadoPorNomina,

        [Description("EN VP GH")]
        EnVpGh,

        [Description("APROBADO POR VP GH")]
        AprobadoPorVpGh,

        [Description("APROBADA")]
        Aprobada,

        [Description("RECHAZADA")]
        Rechazada,

        [Description("CERRADO")]
        Cerrado,

        [Description("RECHAZADO POR RRHH")]
        RechazadoPorRrhh,

        [Description("RECHAZADO POR SELECCION")]
        RechazadoPorSeleccion,

        [Description("RECHAZADO POR NOMINA")]
        RechazadoPorNomina,

        [Description("RECHAZADO POR VP GH")]
        RechazadoPorVpGh
    }
}
