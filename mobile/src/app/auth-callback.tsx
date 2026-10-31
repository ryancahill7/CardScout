import { Redirect } from 'expo-router';

// Browser sign-in returns to <scheme>://auth-callback?code=... The in-app browser session normally
// captures that URL itself; if the OS opens the app with it instead, this just goes home and the
// auth gate shows whichever side (signed in or not) applies.
export default function AuthCallback() {
  return <Redirect href="/" />;
}
