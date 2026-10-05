export type ReminderPreferences = {
  enabled: boolean;
  morning: boolean;
  evening: boolean;
  morningAt: string;
  eveningAt: string;
};

export function reminderTimePatch(prefs: ReminderPreferences, times: Pick<ReminderPreferences, "morningAt" | "eveningAt">) {
  const valid = (value: string) => /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value);
  if (!valid(times.morningAt) || !valid(times.eveningAt)) return null;
  return {
    notifyTime1: prefs.enabled && prefs.evening ? times.eveningAt : null,
    notifyTime2: prefs.enabled && prefs.morning ? times.morningAt : null,
  };
}

export function projectAccountReminders(
  settings: { notifyTime1: string | null; notifyTime2: string | null },
  guest: ReminderPreferences,
): ReminderPreferences {
  return {
    enabled: !!settings.notifyTime1 || !!settings.notifyTime2,
    morning: !!settings.notifyTime2,
    evening: !!settings.notifyTime1,
    morningAt: settings.notifyTime2 || guest.morningAt,
    eveningAt: settings.notifyTime1 || guest.eveningAt,
  };
}
