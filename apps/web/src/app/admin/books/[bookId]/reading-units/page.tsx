import { AdminUnits } from "@/components/admin/admin-units";

export default async function AdminUnitsPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <AdminUnits bookId={bookId} />;
}
