using System.ComponentModel;

namespace BackendRequisicionPersonal.Models.Enums
{
    public enum NivelAprobacion
    {
        [Description("1")]
        Nivel1,

        [Description("2")]
        Nivel2,

        [Description("3")]
        Nivel3,

        [Description("FINAL")]
        Final
    }
}
