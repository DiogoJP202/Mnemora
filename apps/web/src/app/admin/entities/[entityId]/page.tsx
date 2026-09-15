import { AdminEntityEditor } from "@/components/admin/admin-entity-editor";

export default async function AdminEntityPage({ params }: { params: Promise<{ entityId: string }> }) {
  const { entityId } = await params;
  return <AdminEntityEditor entityId={entityId} />;
}
