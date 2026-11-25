using System.Globalization;
using Local133.Application.Notifications;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.Infrastructure.Email;

/// <summary>Correos de avisos al vendedor que salen de webhooks y jobs (sin petición HTTP).</summary>
public static class MerchantEmails
{
    public const string SubscriptionPaidSubject = "Recibimos tu pago de Pro · Local133";

    /// <summary>Ruta del botón del correo de pago: «Mi plan».</summary>
    public const string PlanPath = "/app/plan";

    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    /// <param name="greetingName">Con quién saludar (<c>Merchant.GreetingNameFor</c>); <c>null</c> ⇒ «¡Hola!».</param>
    /// <param name="lastProDay">Último día con Pro (en México).</param>
    /// <param name="extendedExistingPro">Ya tenía Pro pagado vigente: el pago extiende su plan.</param>
    public static string SubscriptionPaidBody(
        string? greetingName, SubscriptionPackage package, decimal amount, DateOnly lastProDay, bool extendedExistingPro,
        int? founderNumber, string siteBaseUrl)
    {
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var greeting = Greeting(greetingName);
        var until = lastProDay.ToString("d 'de' MMMM 'de' yyyy", Mexico);
        var paragraphs = new List<string>
        {
            greeting,
            $"Recibimos tu pago de {amount.ToString("$#,0.00", Mexico)} por Pro ({package.Label().ToLower(Mexico)}).",
            $"Tienes Pro hasta el {until}.",
        };
        if (extendedExistingPro)
        {
            paragraphs.Add("Ya tenías Pro pagado: este pago extiende tu plan, no se pierde ningún día.");
        }

        if (founderNumber is { } number)
        {
            paragraphs.Add($"Eres el fundador número {number.ToString(Mexico)}: conservas el precio de fundador de por vida.");
        }

        return EmailLayout.Render(new EmailContent(
            SubscriptionPaidSubject,
            Preheader: $"Tu plan Pro quedó activo hasta el {until}.",
            Title: "¡Gracias por tu pago!",
            Paragraphs: paragraphs,
            ActionText: "Ver mi plan",
            ActionUrl: site + PlanPath,
            Footer: "Recibiste este correo porque pagaste Pro en Local133. El precio incluye IVA."),
            site);
    }

    private const string ReminderFooter =
        "Recibiste este correo porque tienes tu negocio en Local133. Los precios incluyen IVA.";

    /// <summary>
    /// Aviso y correo de un recordatorio de la suscripción (5.7). El botón lleva a «Mi plan», donde el vendedor elige
    /// su paquete y ahí se genera el link de pago: el correo nunca trae una liga de Pey ni el job crea links.
    /// </summary>
    public static SubscriptionReminderMessage SubscriptionReminder(
        SubscriptionReminder reminder, string? greetingName, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        ArgumentNullException.ThrowIfNull(settings);
        var greeting = Greeting(greetingName);
        var lastDay = LongDate(reminder.LastProDay);
        var graceDay = reminder.LastGraceDay is { } g ? LongDate(g) : null;
        const string choosePackage = "Entra a «Mi plan» y elige tu paquete para renovar.";
        string FreePlan(string intro) =>
            $"{intro} sigues cobrando por monto con QR o por celular, hasta {settings.FreeMonthlyPaidLimit.ToString("N0", Mexico)} cobros al mes. "
            + $"Tu catálogo queda de solo lectura, sin cobro con carrito ni WhatsApp en el recibo, y ves el historial de los últimos {settings.FreeHistoryDays.ToString(Mexico)} días.";
        var noDaysLost = "Si pagas antes, no pierdes ningún día: lo nuevo empieza cuando termine lo que tienes.";

        string title, body, subject, emailTitle, action = "Ver mi plan";
        var paragraphs = new List<string> { greeting };
        switch (reminder.Stage)
        {
            case SubscriptionReminderStage.AnnualUpcoming:
                title = $"Tu plan anual vence el {lastDay}";
                body = $"Renueva Pro antes del {lastDay} para seguir sin interrupciones. {choosePackage}";
                subject = $"Tu plan anual de Pro vence el {lastDay} · Local133";
                emailTitle = "Tu plan anual vence pronto";
                paragraphs.Add($"Tu plan Pro anual vence el {lastDay}, en {reminder.DaysLeft.ToString(Mexico)} días.");
                paragraphs.Add($"{choosePackage} Si renuevas el anual antes de que terminen tus días de gracia, conservas tu precio.");
                paragraphs.Add(noDaysLost);
                action = "Renovar mi plan";
                break;

            case SubscriptionReminderStage.Upcoming when reminder.EndingKind == SubscriptionPeriodKind.Paid:
                title = $"Tu plan Pro vence {When(reminder.DaysLeft)}";
                body = $"Vence el {lastDay}. Paga en «Mi plan» para no perder Pro.";
                subject = $"Tu plan Pro vence {When(reminder.DaysLeft)} · Local133";
                emailTitle = "Tu plan Pro vence pronto";
                paragraphs.Add($"Tu plan Pro vence el {lastDay}.");
                paragraphs.Add(choosePackage);
                paragraphs.Add(noDaysLost);
                if (graceDay is not null)
                {
                    paragraphs.Add($"Si no alcanzas, tienes hasta el {graceDay} para pagar sin perder Pro.");
                }

                action = "Pagar Pro";
                break;

            case SubscriptionReminderStage.Upcoming when reminder.EndingKind == SubscriptionPeriodKind.Trial:
                title = $"Tu prueba termina {When(reminder.DaysLeft)}";
                body = $"Tu prueba de Pro termina el {lastDay}. Después pasas al plan gratuito; paga Pro en «Mi plan» para conservar todo.";
                subject = $"Tu prueba de Pro termina {When(reminder.DaysLeft)} · Local133";
                emailTitle = "Tu prueba termina pronto";
                paragraphs.Add($"Tu prueba de Pro termina el {lastDay}. Después pasas al plan gratuito.");
                paragraphs.Add(FreePlan("En el plan gratuito"));
                paragraphs.Add("Paga Pro en «Mi plan» y sigues con todo. Lo que pagues empieza cuando termine tu prueba: no pierdes ningún día.");
                action = "Conocer Pro";
                break;

            case SubscriptionReminderStage.Upcoming:
                title = $"Tus días de Pro terminan {When(reminder.DaysLeft)}";
                body = $"Tus días de Pro regalados terminan el {lastDay}. Después pasas al plan gratuito; paga Pro en «Mi plan» para seguir con todo.";
                subject = $"Tus días de Pro terminan {When(reminder.DaysLeft)} · Local133";
                emailTitle = "Tus días de Pro terminan pronto";
                paragraphs.Add($"Tus días de Pro regalados terminan el {lastDay}. Después pasas al plan gratuito.");
                paragraphs.Add(FreePlan("En el plan gratuito"));
                paragraphs.Add("Paga Pro en «Mi plan» y sigues con todo: elige tu paquete y ahí mismo pagas.");
                paragraphs.Add(noDaysLost);
                action = "Pagar Pro";
                break;

            case SubscriptionReminderStage.DueToday:
                title = "Tu plan Pro vence hoy";
                body = graceDay is null
                    ? "Paga en «Mi plan» para no perder Pro."
                    : $"Tienes hasta el {graceDay} para pagar sin perder Pro.";
                subject = "Tu plan Pro vence hoy · Local133";
                emailTitle = "Tu plan Pro vence hoy";
                paragraphs.Add($"Tu plan Pro vence hoy, {lastDay}.");
                if (graceDay is not null)
                {
                    paragraphs.Add($"Tienes hasta el {graceDay} para pagar sin perder Pro. Lo que pagues cuenta desde hoy: no se pierden días.");
                }

                paragraphs.Add(choosePackage);
                action = "Pagar Pro";
                break;

            case SubscriptionReminderStage.Grace:
                title = reminder.DaysLeft switch
                {
                    <= 0 => "Hoy es el último día para pagar Pro",
                    1 => "Te queda 1 día para pagar Pro",
                    _ => $"Te quedan {reminder.DaysLeft.ToString(Mexico)} días para pagar Pro",
                };
                body = $"Tu plan venció el {lastDay}. Paga a más tardar el {graceDay} para no pasar al plan gratuito.";
                subject = $"{title} · Local133";
                emailTitle = "No pierdas tu plan Pro";
                paragraphs.Add($"Tu plan Pro venció el {lastDay} y tienes hasta el {graceDay} para pagar sin perder Pro.");
                paragraphs.Add(choosePackage);
                paragraphs.Add(FreePlan("Si no pagas, pasas al plan gratuito. Ahí"));
                action = "Pagar Pro";
                break;

            case SubscriptionReminderStage.Downgraded:
                title = "Pasaste al plan gratuito";
                body = "Conservas tu catálogo, tu historial y tus datos. Paga Pro en «Mi plan» cuando quieras.";
                subject = "Pasaste al plan gratuito · Local133";
                emailTitle = "Pasaste al plan gratuito";
                paragraphs.Add(reminder.EndingKind switch
                {
                    SubscriptionPeriodKind.Paid => $"Tu plan Pro venció el {lastDay} y terminaron tus días de gracia.",
                    SubscriptionPeriodKind.Trial => $"Tu prueba de Pro terminó el {lastDay}.",
                    _ => $"Tus días de Pro regalados terminaron el {lastDay}.",
                });
                paragraphs.Add(FreePlan("En el plan gratuito"));
                paragraphs.Add("Conservas tu catálogo, tu historial y tus datos. Cuando pagues Pro vuelves a tener todo desde ese día.");
                action = "Volver a Pro";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(reminder), reminder.Stage, "Etapa desconocida.");
        }

        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(subject, body, emailTitle, paragraphs, action, PlanPath, ReminderFooter));
    }

    /// <summary>Ruta de «Invita y gana» (5.8).</summary>
    public const string InvitePath = "/app/invita";

    /// <summary>
    /// Aviso y correo a quien invitó cuando su referido paga por primera vez (5.8): ganó sus días, los recibe al
    /// activarse su cuenta o no hubo premio (tope de 12 en 12 meses o cuenta dada de baja).
    /// </summary>
    /// <param name="referredName">Primer nombre del referido; <c>null</c> ⇒ «Tu invitado».</param>
    /// <param name="lastProDay">Último día de Pro después del premio (solo si se otorgó).</param>
    public static SubscriptionReminderMessage ReferralReward(
        ReferralRewardDecision decision, ReferralSkipReason? reason, string? greetingName, string? referredName,
        SystemSettings settings, DateOnly? lastProDay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var who = string.IsNullOrWhiteSpace(referredName) ? "Tu invitado" : referredName.Trim();
        var days = settings.ReferralRewardDays == 1 ? "1 día" : $"{settings.ReferralRewardDays.ToString(Mexico)} días";
        var paragraphs = new List<string> { Greeting(greetingName) };
        string title, body, subject;
        switch (decision)
        {
            case ReferralRewardDecision.Grant:
                title = $"¡Ganaste {days} de Pro!";
                body = lastProDay is { } last
                    ? $"{who} pagó su plan con tu código. Ahora tienes Pro hasta el {LongDate(last)}."
                    : $"{who} pagó su plan con tu código.";
                subject = $"Ganaste {days} de Pro · Local133";
                paragraphs.Add(body);
                paragraphs.Add("Sigue invitando: cada persona que pague con tu código te da más días de Pro.");
                break;

            case ReferralRewardDecision.Pending:
                title = $"Ganaste {days} de Pro";
                body = $"{who} pagó su plan con tu código. Tus {days} se suman en cuanto tu cuenta bancaria esté activa, después de tu prueba.";
                subject = $"Ganaste {days} de Pro · Local133";
                paragraphs.Add(body);
                break;

            default:
                title = $"{who} pagó con tu código";
                body = reason == ReferralSkipReason.ReferrerDisabled
                    ? "Tu cuenta está dada de baja, así que esta vez no hubo días de Pro."
                    : $"Ya ganaste {settings.ReferralRewardsPerYear.ToString(Mexico)} premios en los últimos 12 meses, el máximo: esta vez no hubo días de Pro.";
                subject = $"{who} pagó con tu código · Local133";
                paragraphs.Add($"{who} pagó su plan con tu código. ¡Gracias por recomendar Local133!");
                paragraphs.Add(body);
                break;
        }

        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(subject, body, title, paragraphs, "Ver mis invitaciones", InvitePath, ReminderFooter));
    }

    /// <summary>
    /// Aviso y correo cuando el admin le regala días de Pro (6.2). El motivo que escribió el admin no va aquí.
    /// </summary>
    /// <param name="lastProDay">Último día con Pro después del regalo (en México).</param>
    public static SubscriptionReminderMessage ProDaysGranted(string? greetingName, int days, DateOnly lastProDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var daysText = days == 1 ? "1 día" : $"{days.ToString(Mexico)} días";
        var title = $"Te regalamos {daysText} de Pro";
        var body = $"Ahora tienes Pro hasta el {LongDate(lastProDay)}.";
        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(
                $"{title} · Local133",
                body,
                title,
                [Greeting(greetingName), $"Te regalamos {daysText} de Pro. {body}", "Gracias por cobrar con Local133."],
                "Ver mi plan",
                PlanPath,
                ReminderFooter));
    }

    /// <summary>Ruta del botón del resumen mensual en Pro: el historial de ventas.</summary>
    public const string SalesPath = "/app/ventas";

    /// <summary>
    /// Resumen del mes que terminó (5.10, §7): lo cobrado, la comisión que habría cobrado una terminal (estimación) y el
    /// precio de Pro. En Pro, prueba o gracia el botón va a las ventas; en el gratuito, a «Mi plan» con lo que se pierde.
    /// </summary>
    /// <param name="month">Día 1 del mes (en México).</param>
    /// <param name="isFree">El vendedor está en el gratuito hoy.</param>
    public static SubscriptionReminderMessage MonthlySummary(
        Domain.Subscriptions.MonthlySummary summary, DateOnly month, bool isFree, string? greetingName)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var monthName = month.ToString("MMMM", Mexico);
        var sales = summary.SalesCount == 1 ? "1 venta" : $"{summary.SalesCount.ToString("#,0", Mexico)} ventas";
        var collected = $"En {monthName} cobraste {Money(summary.Collected)} en {sales}.";
        var rate = (summary.FeeRate * 100).ToString("0.##", Mexico);
        var fee = summary.EstimatedTerminalFee >= 1
            ? $"Con una terminal habrías pagado unos {Money(summary.EstimatedTerminalFee)} de comisión (estimación con {rate} % + IVA)."
            : null;
        var price = Money(summary.ProMonthlyPrice);
        var plan = isFree
            ? $"Con Pro, por {price} al mes, cobras con tus productos, ves tu historial completo y tienes tu catálogo en línea."
            : $"Tu plan Pro cuesta {price} al mes.";

        var body = string.Join(' ', new[] { collected, fee, plan }.Where(p => p is not null));
        var title = $"Tu resumen de {monthName}";
        var paragraphs = new List<string> { Greeting(greetingName), collected };
        if (fee is not null)
        {
            paragraphs.Add(fee);
        }

        paragraphs.Add(plan);
        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(
                $"Tu resumen de {monthName} · Local133",
                collected,
                $"Así te fue en {monthName}",
                paragraphs,
                isFree ? "Conoce Pro" : "Ver mis ventas",
                isFree ? PlanPath : SalesPath,
                $"Recibiste este correo porque cobraste con Local133 en {monthName}. Los precios incluyen IVA."));
    }

    /// <summary>«$18,400» si no hay centavos; si los hay, «$18,400.50».</summary>
    private static string Money(decimal amount) =>
        amount.ToString(amount == decimal.Truncate(amount) ? "$#,0" : "$#,0.00", Mexico);

    /// <summary>«en 3 días», «mañana» u «hoy».</summary>
    private static string When(int daysLeft) => daysLeft switch
    {
        <= 0 => "hoy",
        1 => "mañana",
        _ => $"en {daysLeft.ToString(Mexico)} días",
    };

    /// <summary>«¡Hola, María!» o, sin nombre, «¡Hola!».</summary>
    private static string Greeting(string? name) => string.IsNullOrWhiteSpace(name) ? "¡Hola!" : $"¡Hola, {name.Trim()}!";

    private static string LongDate(DateOnly day) => day.ToString("d 'de' MMMM 'de' yyyy", Mexico);

    public const string BankAccountActiveSubject = "Tu cuenta ya está activa: ya puedes cobrar · Local133";

    /// <summary>Ruta del botón: la pantalla de cobro (sin sesión, el sitio manda al login y regresa).</summary>
    public const string ChargePath = "/app/cobrar";

    /// <param name="greetingName">Con quién saludar (<c>Merchant.GreetingNameFor</c>); <c>null</c> ⇒ «¡Hola!».</param>
    /// <param name="maskedClabe">CLABE enmascarada (<c>Clabe.Mask</c>); nunca la completa.</param>
    /// <param name="siteBaseUrl">Base absoluta del sitio.</param>
    public static string BankAccountActiveBody(string? greetingName, string maskedClabe, string siteBaseUrl)
    {
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var greeting = Greeting(greetingName);
        return EmailLayout.Render(new EmailContent(
            BankAccountActiveSubject,
            Preheader: "Tu CLABE quedó lista en CoDi. Entra y haz tu primer cobro.",
            Title: "¡Ya puedes cobrar!",
            Paragraphs:
            [
                greeting,
                $"Tu cuenta bancaria {maskedClabe} ya está activa en CoDi.",
                "Desde hoy puedes cobrar con QR o enviar el cobro al celular de tu cliente. El dinero llega directo a tu cuenta.",
            ],
            ActionText: "Empezar a cobrar",
            ActionUrl: site + ChargePath,
            Footer: "Recibiste este correo porque registraste tu negocio en Local133."),
            site);
    }
}

/// <summary>Recordatorio de la suscripción: título y texto del aviso en el sitio, y su correo.</summary>
public sealed record SubscriptionReminderMessage(string Title, string Body, NotificationEmail Email);
