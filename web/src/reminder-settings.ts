export type ReminderPreferences = {
  enabled: boolean;
  morning: boolean;
  evening: boolean;
  morningAt: string;
  eveningAt: string;
};

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
