import type { Metadata } from "next";
import { AuthForm } from "@/components/reader/auth-form";
export const metadata: Metadata = { title: "Entrar" };
export default function LoginPage() { return <AuthForm mode="login" />; }
