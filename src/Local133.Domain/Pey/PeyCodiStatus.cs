namespace Local133.Domain.Pey;

/// <summary>Estatus CoDi de una cuenta bancaria registrada en Pey (<c>codiStatus</c>).</summary>
/// <remarks>En el cable va en mayúsculas con guion bajo (<c>PENDING</c>, <c>ACTIVE</c>…).</remarks>
public enum PeyCodiStatus
{
    /// <summary>Recibida por Pey, aún no enviada a Banxico.</summary>
    Pending,

    /// <summary>Registro con Banxico en trámite.</summary>
    Processing,

    /// <summary>Registrada: puede recibir cobros CoDi.</summary>
    Active,

    /// <summary>El registro falló; el motivo viene en <c>errorMessage</c>.</summary>
    Error,
}
