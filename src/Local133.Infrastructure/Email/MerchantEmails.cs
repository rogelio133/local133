using System.Globalization;
using Local133.Application.Notifications;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.Infrastructure.Email;

/// <summary>Correos de avisos al vendedor que salen de webhooks y jobs (sin petición HTTP).</summary>
public static class MerchantEmails
{
    public const string SubscriptionPaidSubject = "Recibimos tu pago · Local133";

    /// <summary>Ruta de «Mi plan» (botón del aviso de días regalados por el admin).</summary>
    public const string PlanPath = SubscriptionPaths.Plan;

    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>
    /// Confirmación del pedido pagado (5.15): número, plan, periodo y total, con «Ver comprobante» como primera liga.
    /// </summary>
    /// <param name="greetingName">Con quién saludar (<c>Merchant.GreetingNameFor</c>); <c>null</c> ⇒ «¡Hola!».</param>
    public static string SubscriptionPaidBody(string? greetingName, SubscriptionPaymentCredited payment, string siteBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var order = SubscriptionPaths.OrderNumber(payment.ChargeId);
        var plan = payment.Plan.Label();
        var first = LongDate(MexicoTime.DateOf(payment.PeriodStartsAt));
        var last = LongDate(MexicoTime.DateOf(payment.PeriodEndsAt.AddTicks(-1)));
        var paragraphs = new List<string>
        {
            Greeting(greetingName),
            $"Recibimos tu pago de {payment.Amount.ToString("$#,0.00", Mexico)} del pedido {order}: {plan} · {payment.Package.Label().ToLower(Mexico)}.",
            $"Tu plan {plan} va del {first} al {last}.",
        };
        if (payment.Conversion is { BasicTime.Ticks: > 0 } conversion)
        {
            paragraphs.Add(
                $"Subiste a Pro: tus {DaysText(conversion.BasicTime)} de Básico que quedaban se volvieron {DaysText(conversion.ProTime)} de Pro desde ese momento, y lo que pagaste empieza después.");
        }
        else if (payment.StartsLater)
        {
            paragraphs.Add("Empieza cuando termina lo que ya tenías: no se pierde ningún día.");
        }

        if (payment.FounderNumber is { } number)
        {
            paragraphs.Add($"Eres el fundador número {number.ToString(Mexico)}: conservas el precio de fundador de por vida.");
        }

        return EmailLayout.Render(new EmailContent(
            SubscriptionPaidSubject,
            Preheader: $"Tu pedido {order} está pagado: {plan} del {first} al {last}.",
            Title: "¡Gracias por tu pago!",
            Paragraphs: paragraphs,
            ActionText: "Ver comprobante",
            ActionUrl: site + SubscriptionPaths.Receipt(payment.ChargeId),
            Footer: "Recibiste este correo porque pagaste un plan en Local133. El precio incluye IVA. Este correo no es una factura."),
            site);
    }

    /// <summary>«9 días», «1 día» o «menos de un día» (días completos, hacia abajo).</summary>
    private static string DaysText(TimeSpan time)
    {
        var days = (int)Math.Floor(time.TotalDays);
        return days switch
        {
            0 => "menos de un día",
            1 => "1 día",
            _ => $"{days.ToString(Mexico)} días",
        };
    }

    private const string ReminderFooter =
        "Recibiste este correo porque tienes tu negocio en Local133. Los precios incluyen IVA.";

    /// <summary>
    /// Aviso y correo de un recordatorio de la suscripción (5.7; por plan desde la 5.16). Los textos dicen el plan que
    /// vence (<see cref="Domain.Subscriptions.SubscriptionReminder.EndingPlan"/>). El botón lleva al checkout
    /// (<paramref name="actionPath"/>: el resumen de su último pedido o el paso 1), donde el link de pago nace solo al
    /// pulsar «Pagar»: el correo nunca trae una liga de Pey ni el job crea links.
    /// </summary>
    /// <param name="isFounder">Para los precios que se mencionan (de fundador si lo es).</param>
    /// <param name="actionPath">Ruta del botón (<c>SubscriptionPaths.ChoosePlanFor</c>).</param>
    public static SubscriptionReminderMessage SubscriptionReminder(
        SubscriptionReminder reminder, string? greetingName, SystemSettings settings, bool isFounder, string actionPath)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionPath);
        var greeting = Greeting(greetingName);
        var plan = reminder.EndingPlan.Label();
        var lastDay = LongDate(reminder.LastActiveDay);
        var graceDay = reminder.LastGraceDay is { } g ? LongDate(g) : null;
        const string renewOnline = "Renuévalo en línea en un par de pasos: revisas tu pedido y pagas con CoDi o tarjeta.";
        // 5.14: sin plan gratuito. Al vencer ya no se generan cobros; lo demás se conserva.
        const string expiredPlan =
            "Con el plan vencido ya no puedes generar cobros nuevos. Conservas tu catálogo, tu historial y tus datos, y vuelves a cobrar en cuanto pagues.";
        const string noDaysLost = "Si pagas antes, no pierdes ningún día: lo nuevo empieza cuando termine lo que tienes.";
        var prices = PlansPriceText(settings, isFounder);
        var renewAction = $"Renovar mi plan {plan}";
        const string chooseAction = "Elegir mi plan";

        string title, body, subject, emailTitle, action;
        var paragraphs = new List<string> { greeting };
        switch (reminder.Stage)
        {
            case SubscriptionReminderStage.AnnualUpcoming:
                title = $"Tu plan {plan} Anual vence el {lastDay}";
                body = $"Renueva {plan} antes del {lastDay} para seguir sin interrupciones.";
                subject = $"Tu plan {plan} Anual vence el {lastDay} · Local133";
                emailTitle = "Tu plan anual vence pronto";
                paragraphs.Add($"Tu plan {plan} Anual vence el {lastDay}, en {reminder.DaysLeft.ToString(Mexico)} días.");
                paragraphs.Add($"{renewOnline} Si renuevas el Anual de {plan} antes de que terminen tus días de gracia, conservas tu precio.");
                paragraphs.Add(noDaysLost);
                action = renewAction;
                break;

            case SubscriptionReminderStage.Upcoming when reminder.EndingKind == SubscriptionPeriodKind.Paid:
                title = $"Tu plan {plan} vence {When(reminder.DaysLeft)}";
                body = $"Vence el {lastDay}. Renuévalo para no perder {plan}.";
                subject = $"Tu plan {plan} vence {When(reminder.DaysLeft)} · Local133";
                emailTitle = $"Tu plan {plan} vence pronto";
                paragraphs.Add($"Tu plan {plan} vence el {lastDay}.");
                paragraphs.Add(renewOnline);
                paragraphs.Add(noDaysLost);
                if (graceDay is not null)
                {
                    paragraphs.Add($"Si no alcanzas, tienes hasta el {graceDay} para pagar sin perder {plan}.");
                }

                action = renewAction;
                break;

            case SubscriptionReminderStage.Upcoming when reminder.EndingKind == SubscriptionPeriodKind.Trial:
                title = $"Tu prueba termina {When(reminder.DaysLeft)}";
                body = $"Tu prueba de Pro termina el {lastDay}. Después tu plan vence y ya no puedes cobrar: elige Básico o Pro para seguir.";
                subject = $"Tu prueba de Pro termina {When(reminder.DaysLeft)} · Local133";
                emailTitle = "Tu prueba termina pronto";
                paragraphs.Add($"Tu prueba de Pro termina el {lastDay}. Después tu plan vence.");
                paragraphs.Add(expiredPlan);
                paragraphs.Add($"{prices} Lo que pagues empieza cuando termine tu prueba: no pierdes ningún día.");
                action = chooseAction;
                break;

            case SubscriptionReminderStage.Upcoming:
            {
                var days = DaysName(reminder.EndingKind, plan);
                title = $"Tus {days} terminan {When(reminder.DaysLeft)}";
                body = $"Tus {days} terminan el {lastDay}. Después tu plan vence y ya no puedes cobrar: elige Básico o Pro para seguir.";
                subject = $"Tus {days} terminan {When(reminder.DaysLeft)} · Local133";
                emailTitle = $"Tus {days} terminan pronto";
                paragraphs.Add($"Tus {days} terminan el {lastDay}. Después tu plan vence.");
                paragraphs.Add(expiredPlan);
                paragraphs.Add(prices);
                paragraphs.Add(noDaysLost);
                action = chooseAction;
                break;
            }

            case SubscriptionReminderStage.DueToday:
                title = $"Tu plan {plan} vence hoy";
                body = graceDay is null
                    ? $"Renuévalo hoy para no perder {plan}."
                    : $"Tienes hasta el {graceDay} para pagar sin perder {plan}.";
                subject = $"Tu plan {plan} vence hoy · Local133";
                emailTitle = $"Tu plan {plan} vence hoy";
                paragraphs.Add($"Tu plan {plan} vence hoy, {lastDay}.");
                if (graceDay is not null)
                {
                    paragraphs.Add($"Tienes hasta el {graceDay} para pagar sin perder {plan}. Lo que pagues cuenta desde hoy: no se pierden días.");
                }

                paragraphs.Add(renewOnline);
                action = renewAction;
                break;

            case SubscriptionReminderStage.Grace:
                title = reminder.DaysLeft switch
                {
                    <= 0 => $"Hoy es el último día para pagar {plan}",
                    1 => $"Te queda 1 día para pagar {plan}",
                    _ => $"Te quedan {reminder.DaysLeft.ToString(Mexico)} días para pagar {plan}",
                };
                body = $"Tu plan {plan} venció el {lastDay}. Paga a más tardar el {graceDay} para seguir cobrando.";
                subject = $"{title} · Local133";
                emailTitle = $"No pierdas tu plan {plan}";
                paragraphs.Add($"Tu plan {plan} venció el {lastDay} y tienes hasta el {graceDay} para pagar sin perder {plan}.");
                paragraphs.Add(renewOnline);
                paragraphs.Add($"Si no pagas a tiempo, terminan tus días de gracia. {expiredPlan}");
                action = renewAction;
                break;

            case SubscriptionReminderStage.Expired:
                title = "Tu plan venció: ya no puedes cobrar";
                body = "Conservas tu catálogo, tu historial y tus datos. Elige un plan para volver a cobrar.";
                subject = "Tu plan venció · Local133";
                emailTitle = "Tu plan venció";
                paragraphs.Add(reminder.EndingKind switch
                {
                    SubscriptionPeriodKind.Paid => $"Tu plan {plan} venció el {lastDay} y terminaron tus días de gracia.",
                    SubscriptionPeriodKind.Trial => $"Tu prueba de Pro terminó el {lastDay}.",
                    _ => $"Tus {DaysName(reminder.EndingKind, plan)} terminaron el {lastDay}.",
                });
                paragraphs.Add(expiredPlan);
                paragraphs.Add(prices);
                action = chooseAction;
                break;

            case SubscriptionReminderStage.ExpiredFollowUp:
                title = "Tus clientes te siguen buscando";
                body = "Elige un plan para volver a cobrar. Tu catálogo, tu historial y tus datos te esperan.";
                subject = "Tus clientes te siguen buscando · Local133";
                emailTitle = "Vuelve a cobrar con Local133";
                paragraphs.Add($"Tu plan venció el {lastDay}: desde entonces no puedes generar cobros nuevos.");
                paragraphs.Add("Tu catálogo, tu historial y tus datos siguen aquí. En cuanto pagues, vuelves a cobrar con QR o al celular de tus clientes.");
                paragraphs.Add(prices);
                action = chooseAction;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(reminder), reminder.Stage, "Etapa desconocida.");
        }

        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(subject, body, emailTitle, paragraphs, action, actionPath, ReminderFooter));
    }

    /// <summary>«días de Básico de transición», «días de Pro regalados» o «días de Pro» (conversión).</summary>
    private static string DaysName(SubscriptionPeriodKind kind, string plan) => kind switch
    {
        SubscriptionPeriodKind.Transition => $"días de {plan} de transición",
        SubscriptionPeriodKind.ReferralReward or SubscriptionPeriodKind.AdminGrant => $"días de {plan} regalados",
        _ => $"días de {plan}",
    };

    /// <summary>«Básico cuesta $99 al mes y Pro $199 al mes.» (de fundador si lo es).</summary>
    private static string PlansPriceText(SystemSettings settings, bool isFounder)
    {
        var basic = Money(SubscriptionPricing.MonthlyPrice(SubscriptionPlan.Basic, settings, isFounder));
        var pro = Money(SubscriptionPricing.MonthlyPrice(SubscriptionPlan.Pro, settings, isFounder));
        return isFounder
            ? $"Con tu precio de fundador, Básico cuesta {basic} al mes y Pro {pro} al mes."
            : $"Básico cuesta {basic} al mes y Pro {pro} al mes.";
    }

    /// <summary>Ruta de «Invita y gana» (5.8).</summary>
    public const string InvitePath = "/app/invita";

    /// <summary>
    /// Aviso y correo a quien invitó cuando su referido paga por primera vez (5.8; por plan desde la 5.16): ganó días del
    /// plan del final de su cadena, los recibe al empezar su prueba (con su primer cobro, de Pro) o no hubo premio (tope de
    /// 12 en 12 meses o cuenta dada de baja).
    /// </summary>
    /// <param name="referredName">Primer nombre del referido; <c>null</c> ⇒ «Tu invitado».</param>
    /// <param name="plan">Plan de los días ganados (solo con <see cref="ReferralRewardDecision.Grant"/>).</param>
    /// <param name="reactivated">Su plan estaba vencido y el premio lo reactivó: vuelve a cobrar.</param>
    /// <param name="lastActiveDay">Último día vigente después del premio (solo si se otorgó).</param>
    public static SubscriptionReminderMessage ReferralReward(
        ReferralRewardDecision decision, ReferralSkipReason? reason, string? greetingName, string? referredName,
        SystemSettings settings, SubscriptionPlan plan, bool reactivated, DateOnly? lastActiveDay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var who = string.IsNullOrWhiteSpace(referredName) ? "Tu invitado" : referredName.Trim();
        var days = settings.ReferralRewardDays == 1 ? "1 día" : $"{settings.ReferralRewardDays.ToString(Mexico)} días";
        var paragraphs = new List<string> { Greeting(greetingName) };
        string title, body, subject;
        switch (decision)
        {
            case ReferralRewardDecision.Grant:
            {
                var name = plan.Label();
                title = $"¡Ganaste {days} de {name}!";
                body = (lastActiveDay, reactivated) switch
                {
                    ({ } last, true) => $"{who} pagó su plan con tu código. Ya puedes volver a cobrar: tienes {name} hasta el {LongDate(last)}.",
                    ({ } last, false) => $"{who} pagó su plan con tu código. Ahora tienes {name} hasta el {LongDate(last)}.",
                    _ => $"{who} pagó su plan con tu código.",
                };
                subject = $"Ganaste {days} de {name} · Local133";
                paragraphs.Add(body);
                paragraphs.Add("Sigue invitando: cada persona que pague con tu código te da más días de tu plan.");
                break;
            }

            case ReferralRewardDecision.Pending:
                // Se encadenan después de su prueba, que es de Pro.
                title = $"Ganaste {days} de Pro";
                body = $"{who} pagó su plan con tu código. Tus {days} de Pro se suman al terminar tu prueba, que empieza con tu primer cobro.";
                subject = $"Ganaste {days} de Pro · Local133";
                paragraphs.Add(body);
                break;

            default:
                title = $"{who} pagó con tu código";
                body = reason == ReferralSkipReason.ReferrerDisabled
                    ? "Tu cuenta está dada de baja, así que esta vez no hubo días de premio."
                    : $"Ya ganaste {settings.ReferralRewardsPerYear.ToString(Mexico)} premios en los últimos 12 meses, el máximo: esta vez no hubo días de premio.";
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
    /// Aviso y correo cuando el admin le regala días de un plan (6.2; con plan desde la 5.17). El motivo que escribió el
    /// admin no va aquí. Los días se encadenan al final: si empiezan después de hoy, el texto dice cuándo.
    /// </summary>
    /// <param name="firstDay">Primer día del regalo (en México).</param>
    /// <param name="lastDay">Último día del regalo, que es el último vigente (en México).</param>
    /// <param name="today">Hoy (en México).</param>
    public static SubscriptionReminderMessage DaysGranted(
        string? greetingName, SubscriptionPlan plan, int days, DateOnly firstDay, DateOnly lastDay, DateOnly today)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var daysText = days == 1 ? "1 día" : $"{days.ToString(Mexico)} días";
        var name = plan.Label();
        var title = $"Te regalamos {daysText} de {name}";
        var body = firstDay > today
            ? $"Tus días de {name} van del {LongDate(firstDay)} al {LongDate(lastDay)}."
            : $"Ahora tienes {name} hasta el {LongDate(lastDay)}.";
        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(
                $"{title} · Local133",
                body,
                title,
                [Greeting(greetingName), $"Te regalamos {daysText} de {name}. {body}", "Gracias por cobrar con Local133."],
                "Ver mi plan",
                PlanPath,
                ReminderFooter));
    }

    /// <summary>Ruta del botón del resumen mensual con plan vigente: el historial de ventas.</summary>
    public const string SalesPath = "/app/ventas";

    /// <summary>
    /// Resumen del mes que terminó (5.10, §7; por plan desde la 5.16): lo cobrado, la comisión que habría cobrado una
    /// terminal (estimación) y el precio del plan. Con plan vigente dice cuánto cuesta su plan y el botón va a las
    /// ventas; si venció, «Con Básico pagarías $99 al mes» y el botón va a la sección de Básico del checkout.
    /// </summary>
    /// <param name="month">Día 1 del mes (en México).</param>
    public static SubscriptionReminderMessage MonthlySummary(
        Domain.Subscriptions.MonthlySummary summary, DateOnly month, string? greetingName)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var monthName = month.ToString("MMMM", Mexico);
        var sales = summary.SalesCount == 1 ? "1 venta" : $"{summary.SalesCount.ToString("#,0", Mexico)} ventas";
        var collected = $"En {monthName} cobraste {Money(summary.Collected)} en {sales}.";
        var rate = (summary.FeeRate * 100).ToString("0.##", Mexico);
        var fee = summary.EstimatedTerminalFee >= 1
            ? $"Con una terminal habrías pagado unos {Money(summary.EstimatedTerminalFee)} de comisión (estimación con {rate} % + IVA)."
            : null;
        var price = Money(summary.PlanMonthlyPrice);
        var name = summary.Plan.Label();
        var plan = summary.IsExpired
            ? $"Tu plan venció: con {name} pagarías {price} al mes y vuelves a cobrar con QR o al celular de tus clientes."
            : $"Tu plan {name} cuesta {price} al mes.";

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
                summary.IsExpired ? "Elegir mi plan" : "Ver mis ventas",
                summary.IsExpired ? SubscriptionPaths.ChoosePlan(summary.Plan) : SalesPath,
                $"Recibiste este correo porque cobraste con Local133 en {monthName}. Los precios incluyen IVA."));
    }

    /// <summary>
    /// Anuncio del cambio de planes 2026-12 (§10, 5.16): ya no hay gratuito; qué incluyen Básico y Pro y cuánto cuestan
    /// (de fundador si lo es); qué le pasa a este vendedor (su transición, su prueba o su plan vigente). El botón va al
    /// paso 1 del checkout.
    /// </summary>
    public static SubscriptionReminderMessage PlanChangeAnnouncement(
        string? greetingName, SystemSettings settings, bool isFounder, PlanChangeSituation situation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(situation);
        var basic = Money(SubscriptionPricing.MonthlyPrice(SubscriptionPlan.Basic, settings, isFounder));
        var pro = Money(SubscriptionPricing.MonthlyPrice(SubscriptionPlan.Pro, settings, isFounder));
        string Day() => situation.LastDay is { } day ? LongDate(day) : string.Empty;
        var yours = situation.Kind switch
        {
            PlanChangeSituationKind.Transition =>
                $"Para que tengas tiempo de elegir, te regalamos días de Básico hasta el {Day()}.",
            PlanChangeSituationKind.TrialThenTransition =>
                $"Tu prueba de Pro sigue igual. Al terminar, te regalamos días de Básico hasta el {Day()} para que elijas tu plan.",
            PlanChangeSituationKind.Current when situation.Plan is { } current =>
                $"Tu plan {current.Label()} sigue igual hasta el {Day()}. Después eliges Básico o Pro.",
            PlanChangeSituationKind.Expired => "Tu plan ya venció: elige Básico o Pro para volver a cobrar.",
            _ => $"Tu prueba de {settings.TrialDays.ToString(Mexico)} días de Pro empieza con tu primer cobro. Después eliges Básico o Pro.",
        };

        const string title = "Cambiamos nuestros planes";
        var body = $"Ya no hay plan gratuito: ahora hay Básico ({basic} al mes) y Pro ({pro} al mes). {yours}";
        var paragraphs = new List<string>
        {
            Greeting(greetingName),
            "Cambiamos nuestros planes: ya no hay plan gratuito. Ahora hay dos planes:",
            $"Básico, {basic} al mes: cobras con QR o al celular de tus clientes, ves tu historial completo con búsqueda y haces tu corte de caja.",
            $"Pro, {pro} al mes: todo lo de Básico, más tu catálogo de productos, el cobro con carrito, tu catálogo en línea, la exportación a Excel y el recibo con tu logo.",
        };
        if (isFounder)
        {
            paragraphs.Add("Como fundador, conservas tu precio de fundador en los dos planes.");
        }

        paragraphs.Add(yours);
        paragraphs.Add("Cuando tu plan vence ya no puedes generar cobros nuevos, pero tu catálogo, tu historial y tus datos se quedan.");
        return new SubscriptionReminderMessage(
            title,
            body,
            new NotificationEmail(
                "Nuevos planes: Básico y Pro · Local133",
                body,
                "Nuevos planes en Local133",
                paragraphs,
                "Ver los planes",
                SubscriptionPaths.Checkout,
                ReminderFooter));
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

    /// <summary>«Haz tu primer cobro: ese día empiezan tus 15 días de Pro.» (5.13).</summary>
    public static string FirstChargeTrialText(int trialDays) =>
        $"Haz tu primer cobro: ese día empiezan tus {trialDays.ToString(Mexico)} días de Pro.";

    public const string BankAccountActiveSubject = "Tu cuenta ya está activa: ya puedes cobrar · Local133";

    /// <summary>Ruta del botón: la pantalla de cobro (sin sesión, el sitio manda al login y regresa).</summary>
    public const string ChargePath = "/app/cobrar";

    /// <param name="greetingName">Con quién saludar (<c>Merchant.GreetingNameFor</c>); <c>null</c> ⇒ «¡Hola!».</param>
    /// <param name="maskedClabe">CLABE enmascarada (<c>Clabe.Mask</c>); nunca la completa.</param>
    /// <param name="siteBaseUrl">Base absoluta del sitio.</param>
    /// <param name="trialDays">Días de la prueba de Pro, que empieza con el primer cobro (5.13); <c>null</c> si ya la tuvo.</param>
    public static string BankAccountActiveBody(string? greetingName, string maskedClabe, string siteBaseUrl, int? trialDays = null)
    {
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var greeting = Greeting(greetingName);
        var days = trialDays is > 0 ? trialDays.Value : 0;
        var trial = days > 0 ? FirstChargeTrialText(days) : null;
        var paragraphs = new List<string>
        {
            greeting,
            $"Tu cuenta bancaria {maskedClabe} ya está activa en CoDi.",
            "Desde hoy puedes cobrar con QR o enviar el cobro al celular de tu cliente. El dinero llega directo a tu cuenta.",
        };
        if (trial is not null)
        {
            paragraphs.Add(trial);
        }

        return EmailLayout.Render(new EmailContent(
            BankAccountActiveSubject,
            Preheader: trial is null
                ? "Tu CLABE quedó lista en CoDi. Entra y haz tu primer cobro."
                : $"Tu CLABE quedó lista en CoDi. Con tu primer cobro empiezan tus {days.ToString(Mexico)} días de Pro.",
            Title: "¡Ya puedes cobrar!",
            Paragraphs: paragraphs,
            ActionText: "Empezar a cobrar",
            ActionUrl: site + ChargePath,
            Footer: "Recibiste este correo porque registraste tu negocio en Local133."),
            site);
    }
}

/// <summary>Recordatorio de la suscripción: título y texto del aviso en el sitio, y su correo.</summary>
public sealed record SubscriptionReminderMessage(string Title, string Body, NotificationEmail Email);
