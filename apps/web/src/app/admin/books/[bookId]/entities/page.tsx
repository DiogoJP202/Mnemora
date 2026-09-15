import { AdminEntities } from "@/components/admin/admin-entities";

export default async function AdminEntitiesPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <AdminEntities bookId={bookId} />;
}
