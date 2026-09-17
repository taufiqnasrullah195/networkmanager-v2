# TheWiseNetwork — Advanced Alert Rules + Notification Engine + Escalation (Step 15)

STEP 15 extends the Step 11 Alert Engine into a controlled, decoupled notification system. It is read-only toward
the network and strictly delivery-oriented: an ALERT is a network condition; a NOTIFICATION is a delivery attempt.

## Golden rule

- Monitoring frequency does NOT determine notification frequency: a 30-second poll produces ONE initial notification,
  optional escalation, and one recovery — never a notification storm — while preserving complete monitoring evidence.
- ALERT ≠ NOTIFICATION: alert lifecycle (Open→Acknowledged→Resolved) and notification lifecycle
  (Pending→Sent/Failed/Cancelled) are separate. A FAILED delivery is NEVER an alert resolution, and a delivery failure
  NEVER breaks monitoring.
- The user controls notification configuration; AI can only READ notification history.

## Architecture

```
AlertEngine ── AlertEvent ── NotificationEngine ── NotificationPolicy ── NotificationService ── INotificationChannel
                                 │                                                              (Desktop, Email, Webhook…)
                                 ├─ cooldown / escalation / idempotency / grouping
                                 └─ INotificationStore (SQLite) ── read-only AI tool
```

All of this lives in cross-platform `NETworkManager.AI/Notifications` (testable on Linux); only the concrete Windows
channel is WPF-specific.

## Alert vs notification

`Notification` (NotificationId, AlertId, Channel, Status, EventType, IdempotencyKey, CreatedAt, SentAt, AttemptCount,
FailureReason) is a delivery record. `NotificationStatus` is Pending/Sent/Failed/Cancelled — distinct from
`AlertStatus`.

## Channels

`INotificationChannel` (Name, Description, RequiresCredential, IsAvailable, IsConfigured, `SendAsync`). The initial
channel is **Desktop** (a `DesktopNotificationChannel` that records + logs the delivery; a native toast popup is a
follow-up). Email/webhook are described by the same abstraction and remain optional — only the abstraction + config
boundary exist for them this step.

## Policy

`NotificationPolicy` (configurable `NotificationPolicyConfig`): enabled, minimum severity, per-event-type toggles
(created/recovery/escalation), and severity→channel routing (default "Desktop"). Deterministic; no AI.

## Cooldown

`NotificationEngineOptions.Cooldown` (default 15 min) suppresses a non-recovery notification for the same alert within
the window of the previous notification. It applies to delivery only — never to monitoring evidence.

## Escalation

Deterministic `AlertEscalationRule` (Level, AfterDuration, EscalatedSeverity, Enabled). `EvaluateEscalationAsync(now)`
escalates active alerts once per level (idempotency-keyed). The periodic escalation tick is a follow-up (the method is
implemented + tested and can be driven by a timer).

## Grouping

Grouping metadata is present (`NotificationEngineOptions.GroupingEnabled`/`GroupingWindow`); individual alerts are
never hidden from AlertStore. A grouping summary is a follow-up.

## Queue / retry / idempotency

- The `NotificationService` performs bounded retry per channel (`NotificationRetryOptions`: MaxAttempts=3, exponential
  backoff) with an injectable delay (tests run without real sleeps).
- Idempotency key = `AlertId + EventType + EscalationLevel`. The engine never enqueues the same key twice; a SENT
  notification for a key (persisted) blocks re-delivery across restarts. A FAILED delivery does not block retries.
- Cancellation returns CANCELLED; a channel that is unavailable/not-configured returns FAILED without sending.

## Persistence

Schema v3 adds the `notifications` table (indexed by alert/time + idempotency key). `SqliteNotificationStore` +
`INotificationStore`/`INotificationQuery` provide bounded history queries (filter by alert/channel/status/date range).
No secrets are stored.

## Settings UI

The dashboard surfaces a minimal Notifications panel: channel status, notification history, and a **Test Notification**
button (clearly identified, creates no alert, touches no alert/monitoring state). A full settings editor
(minimum severity, per-event toggles, cooldown, escalation) is a follow-up; the policy is configurable in code via
`NotificationComposition`.

## AI read-only integration

`network_notification_history` (read-only, `INotificationQuery`-only) answers "was a notification sent for the gateway
outage?", "which alerts were notified?", "was the recovery notification delivered?" from structured, secret-free
records. The AI cannot send/disable/configure notifications or modify rules (asserted by tests).

## Security

Notification messages and logs contain no credentials/tokens/passwords; credentials use secure storage (for future
external channels); the test notification does not touch alert state; AI is read-only. All asserted by tests.

## Observability

`INotificationLogger` (ids/status only) records queued/sent/failed/retry/cooldown/escalation/recovery/grouping — no
secrets.
