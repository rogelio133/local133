namespace Local133.Domain.Sales;

/// <summary>Cómo se cobra la venta.</summary>
public enum ChargeMethod
{
    /// <summary>QR CoDi con monto y expiración, generado para esta venta.</summary>
    DynamicQr,

    /// <summary>QR fijo del puesto (el pagador escribe el monto).</summary>
    StaticQr,

    /// <summary>Notificación push al celular del pagador.</summary>
    PushNotification,
}

/// <summary>De dónde sale el monto de la venta.</summary>
public enum SaleSource
{
    /// <summary>Carrito de productos.</summary>
    Cart,

    /// <summary>Monto capturado en el teclado.</summary>
    DirectAmount,

    /// <summary>Pago recibido en el QR fijo (monto abierto).</summary>
    OpenAmount,
}

/// <summary>Estatus de la venta (sus pagos se modelan aparte: un QR puede tener 0..N pagos).</summary>
public enum SaleStatus
{
    /// <summary>Esperando el pago.</summary>
    Pending,

    /// <summary>Pagada.</summary>
    Paid,

    /// <summary>El QR venció sin pago.</summary>
    Expired,

    /// <summary>Cancelada por el vendedor.</summary>
    Cancelled,
}
