using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using UnityEngine.Assertions;

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Mathematics;

namespace Segments
{
	[WorldSystemFilter(0)]
	[UpdateInGroup( typeof(UpdatePresentationSystemGroup) )]
	[UpdateBefore( typeof(SegmentTransformSystem) )]
	public class NativeArrayToSegmentsSystem : SystemBase
	{
		
		List<Batch> _batches = new List<Batch>();

		public NativeList<JobHandle> Dependencies;
		public JobHandle ScheduledJobs => Dependency;


		protected override void OnCreate ()
		{
			Dependencies = new NativeList<JobHandle>( Allocator.Persistent );
		}


		protected override void OnDestroy ()
		{
			Dependency.Complete();
			JobHandle.CombineDependencies( Dependencies ).Complete();
			Dependencies.Dispose();

			foreach( var batch in _batches )
			{
				if( batch.entities.IsCreated )
				{
					EntityManager.DestroyEntity( batch.entities );
					batch.entities.Dispose();
				}
				// if( batch.buffer.IsCreated ) batch.buffer.Dispose();// don't - it's not my responsibility
			}
			_batches.Clear();
		}


		protected override void OnUpdate ()
		{
			if( Dependencies.Length!=0 )
			{
				Dependencies.Add( Dependency );
				Dependency = JobHandle.CombineDependencies( Dependencies );
				Dependencies.Clear();
			}
			if( _batches.Count==0 ) return;

			var entityManager = EntityManager;
			var segmentData = GetComponentDataFromEntity<Segment>( isReadOnly:false );

			for( int batchIndex=_batches.Count-1 ; batchIndex!=-1 ; batchIndex-- )
			{
				var batch = _batches[ batchIndex ];
				NativeArray<float3x2> buffer = batch.buffer;
				NativeArray<Entity> entities = batch.entities;
				int length = batch.length;

				if( buffer.IsCreated )
				{
					Job
						.WithName("component_data_update_job")
						.WithReadOnly( buffer ).WithNativeDisableContainerSafetyRestriction( buffer )
						.WithNativeDisableContainerSafetyRestriction( segmentData )
						.WithCode( () =>
						{
							for( int i=0 ; i<length ; i++ )
								segmentData[ entities[i] ] = new Segment{ start=buffer[i].c0 , end=buffer[i].c1 };
						} )
						.WithBurst().Schedule();
				}
				else if( entities.IsCreated )
				{
					_batches.RemoveAt( batchIndex );
					entityManager.DestroyEntity( entities );
				}
			}
		}


		public void CreateBatch ( in Entity segmentPrefab , in int length , out NativeArray<float3x2> buffer )
		{
			buffer = new NativeArray<float3x2>( length , Allocator.Persistent );
			NativeArray<Entity> entities = EntityManager.Instantiate( segmentPrefab , length , Allocator.Persistent );
			_batches.Add( new Batch{
				prefab		= segmentPrefab ,
				length		= length ,
				entities	= entities ,
				buffer		= buffer
			} );
		}
		

		struct Batch
		{
			public Entity prefab;
			public int length;
			public NativeArray<Entity> entities;
			public NativeArray<float3x2> buffer;
		}


	}
}