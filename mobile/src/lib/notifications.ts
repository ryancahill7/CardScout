import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { AndroidImportance, IosAuthorizationStatus } from 'expo-notifications';
import { router } from 'expo-router';
import { useEffect } from 'react';
import { Platform } from 'react-native';

import { registerPushToken } from './api';

// Show alerts as banners even while the app is open (e.g. browsing a show with the app up)
Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: true,
    shouldSetBadge: false,
  }),
});

export type PushResult = { ok: true; token: string } | { ok: false; reason: string };

// Asks for permission, gets this device's Expo push token and saves it with the API so the
// workers can alert this user. Needs an EAS project id (run `npx eas-cli init` once) and,
// on Android, a development build: Expo Go can't receive remote pushes there.
export async function registerForPush(): Promise<PushResult> {
  if (!Device.isDevice) return { ok: false, reason: 'Push notifications need a real phone, not a simulator.' };

  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync('default', {
      name: 'Deal alerts',
      importance: AndroidImportance.HIGH,
    });
  }

  const existing = await Notifications.getPermissionsAsync();
  let granted = existing.granted || existing.ios?.status === IosAuthorizationStatus.PROVISIONAL;
  if (!granted) {
    const asked = await Notifications.requestPermissionsAsync({ ios: { allowAlert: true, allowBadge: true, allowSound: true } });
    granted = asked.granted;
  }
  if (!granted) return { ok: false, reason: 'Notifications are turned off for CardScout. Turn them on in iOS Settings.' };

  const projectId = Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;
  if (!projectId) {
    return {
      ok: false,
      reason: 'No EAS project id yet. Run `npx eas-cli init` in mobile/ once (it adds extra.eas.projectId to app.json), then restart Expo.',
    };
  }

  try {
    const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
    await registerPushToken(token, Platform.OS);
    return { ok: true, token };
  } catch (e) {
    return { ok: false, reason: `Couldn't get a push token: ${e instanceof Error ? e.message : String(e)}` };
  }
}

// Silently re-registers on launch if the user already allowed notifications (tokens can rotate)
export async function refreshPushRegistration() {
  try {
    const perms = await Notifications.getPermissionsAsync();
    if (perms.granted) await registerForPush();
  } catch {
    // best effort
  }
}

// Opens the right screen when the user taps an alert
export function useNotificationRouting() {
  useEffect(() => {
    function open(data: Record<string, unknown> | undefined) {
      if (!data) return;
      if (data.type === 'card' && data.cardId) {
        router.push({ pathname: '/inventory/[id]', params: { id: String(data.cardId) } }, { withAnchor: true });
      } else if (data.type === 'watchlist-hit' || data.type === 'watchlist') {
        router.navigate('/deals');
      }
    }

    const last = Notifications.getLastNotificationResponse();
    if (last) open(last.notification.request.content.data);

    const sub = Notifications.addNotificationResponseReceivedListener((response) =>
      open(response.notification.request.content.data),
    );
    return () => sub.remove();
  }, []);
}
